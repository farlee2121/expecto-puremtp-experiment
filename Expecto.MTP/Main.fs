module Expecto.MTP
open Expecto
open Microsoft.Testing.Platform.Builder
open Microsoft.Testing.Platform.Capabilities.TestFramework
open System
open System.Collections.Generic
open Microsoft.Testing.Platform.Extensions.TestFramework
open System.Threading.Tasks
open Microsoft.Testing.Platform.Requests
open Microsoft.Testing.Platform.Extensions

module AssemblyVersion =
    open System.Reflection
    let getAssemblyVersion () = 
        match Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
        | null -> "0.0.0"
        | x -> x.InformationalVersion

module SourceLocation = 
    let tryGetCodeLocation assembly (test: Test) : SourceLocation option =
        match test with 
        | Test.TestLabel (_, Test.TestCase (testCode, _), _) -> 
            let location = Expecto.Impl.CodeLocation.getLocation assembly testCode
            if location = SourceLocation.empty 
            then None 
            else Some location
        | _ -> None

    let nullLocationLookup assembly (test: Test) : SourceLocation option =
        None

module Filter = 

    let private sequenceResultM (results: Result<'ok, 'error> seq) : Result<'ok seq, 'error> =
        Seq.fold (fun state next -> 
                state |> Result.bind (fun (successes: 'ok ResizeArray) ->
                    match next with
                    | Ok nextSuccess -> 
                        successes.Add(nextSuccess)
                        Ok successes
                    | Error error -> Error error
                )
            ) 
            (Ok (ResizeArray<'ok>())) results
        |> Result.map (fun l -> l :> 'ok seq)
    let ofMTPFilter (joinWith: string) (mtpFilter: ITestExecutionFilter) : Result<(string list -> bool) option, Exception> =
        let rec recurse (mtpFilter: ITestExecutionFilter) : Result<(string list -> bool) option, Exception> = 
            match mtpFilter with 
            | :? NopFilter ->
                Ok None
                // Ok (fun _ -> true)
            | :? TestNodeUidListFilter as uidFilter->
                // PROBLEM: the MTP TestNodeUid is meant to uniquely identify tests groupings or cases, 
                //          but expecto's Test.filter flattens to testLeafs to filter. 
                //          It's possible we could include extra tests who's name hierarchy flattens the same even if the segments are different 
                (Ok << Some)(fun (nameSegments: string list) -> 
                    let flatName = nameSegments |> String.concat joinWith
                    uidFilter.TestNodeUids 
                    |> Array.exists (fun (uid: Messages.TestNodeUid) -> flatName.StartsWith(uid))
                ) 

            // NOTE: --treenode-filter isn't accepted as a valid cli argument. It's experimental, so there may be special steps to enable it. 
            //       I couldn't test the TestNodeFilter and CompositTestExecutionFilter, so I commented them out

            // | :? TreeNodeFilter as nodeFilter ->
            //     (Ok << Some) (fun (nameSegments: string list) -> 
            //         let flatName = nameSegments |> String.concat joinWith
            //         nodeFilter.MatchesFilter(flatName, Messages.PropertyBag())
            //     ) 

            // | :? CompositeTestExecutionFilter as composite-> 
            //     match composite.Operator with
            //     | TestExecutionFilterOperator.And ->
            //         composite.Filters 
            //         |> Seq.map recurse 
            //         |> sequenceResultM 
            //         |> Result.map  (fun subFilters -> 
            //             Some (fun nameSegmentList -> 
            //                 subFilters |> Seq.forall (fun f -> 
            //                     match f with 
            //                     | Some f -> f nameSegmentList
            //                     | None -> true
            //                 )
            //             )
            //         ) 
            //     | unsupportedOperator -> Error (InvalidOperationException($"Unsupported filter operator {unsupportedOperator}"))
            | unsupported -> 
                let filterTypeName = 
                    match unsupported with 
                    | null -> "null"
                    | unsupported -> unsupported.GetType().FullName
                (InvalidOperationException($"Unsupported filter type '{filterTypeName}'")) :> Exception |> Error

        recurse mtpFilter

module TestNodeUpdates =
    open Microsoft.Testing.Platform.Extensions.Messages
    open Microsoft.Testing.Platform.TestHost

    module TestFileLocationProperty =
        let ofSourceLocation (location: SourceLocation) =
            TestFileLocationProperty(location.sourcePath, LinePositionSpan(LinePosition(location.lineNumber, 0), LinePosition(location.lineNumber, 0)))

        let tryAddLocationProp (maybeLocation: TestFileLocationProperty option) (props: IProperty list) =
            match maybeLocation with 
            | None -> props
            | Some locationProp -> locationProp :: props


    let ofExpectoTest (nameJoiner: string) (tryGetCodeLocation: Test -> SourceLocation option) (sessionId: SessionUid) (properties: IProperty list) (test: Test) : TestNodeUpdateMessage list =
        let rec recurse (parentId: TestNodeUid option) (test: Test) : TestNodeUpdateMessage list =
            match test with 
            | Test.Sequenced (_, childTest) -> recurse parentId childTest
            | Test.TestList (tests, _) -> tests |> List.collect (recurse parentId)
            | Test.TestCase (testcode, _) -> []
            | Test.TestLabel (label, childTest, _) as testNode -> 
                let currentNode =
                    let parentUidString = parentId |> Option.map _.Value |> Option.defaultValue String.Empty
                    // NOTE: the TestNodeUid is expected to be stable across test runs
                    // TODO: Use the separator from the expecto configuration
                    let testFullName = 
                        match parentId with 
                        | None -> label
                        | Some parentUid -> parentUid.Value + nameJoiner + label

                    let maybeCodeLocation = tryGetCodeLocation testNode |> Option.map TestFileLocationProperty.ofSourceLocation

                    let properties = properties |> TestFileLocationProperty.tryAddLocationProp  maybeCodeLocation

                    TestNodeUpdateMessage(
                        sessionId,
                        new TestNode(
                            Uid = TestNodeUid(testFullName), 
                            DisplayName = label, 
                            Properties = PropertyBag(properties)),
                        parentId |> Option.defaultValue null
                    )
                let childNodes = recurse (Some currentNode.TestNode.Uid) childTest
                currentNode :: childNodes

        recurse None test
 
    let overwriteUpdateProps (newProperty: IProperty) (msg: TestNodeUpdateMessage) = 
        TestNodeUpdateMessage(
            sessionUid = msg.SessionUid, 
            testNode = TestNode(Uid = msg.TestNode.Uid, DisplayName = msg.TestNode.DisplayName, Properties = PropertyBag(newProperty)),
            parentTestNodeUid = msg.ParentTestNodeUid)

module private MTPPrinterAdapter =
  open Microsoft.Testing.Platform.Extensions.Messages
  type private TestPrinters = Expecto.Impl.TestPrinters

  let create (expectedResults: TestNodeUpdateMessage list) (publishMessage: TestNodeUpdateMessage -> Async<unit>) =
    let testResultLookup : Map<string, TestNodeUpdateMessage> = 
        expectedResults
        |> List.map (fun msg -> msg.TestNode.Uid.Value, msg)
        |> Map

    let beforeEach name =
        Map.find name testResultLookup
        |> TestNodeUpdates.overwriteUpdateProps (InProgressTestNodeStateProperty())
        |> publishMessage

    // let info _ = async.Zero ()

    let passed (name: string) (duration: TimeSpan) =
        Map.find name testResultLookup
        |> TestNodeUpdates.overwriteUpdateProps PassedTestNodeStateProperty.CachedInstance
        |> publishMessage

    let ignored (name: string) (reason: string) =
        Map.find name testResultLookup
        |> TestNodeUpdates.overwriteUpdateProps (SkippedTestNodeStateProperty($"Skipped: {reason}" ))
        |> publishMessage

    let failed (name: string) (reason: string) (duration: TimeSpan) =
        Map.find name testResultLookup
        |> TestNodeUpdates.overwriteUpdateProps (FailedTestNodeStateProperty(reason))
        |> publishMessage
      
    let exn (name: string) (exn: exn) (duration: TimeSpan) =
        Map.find name testResultLookup
        |> TestNodeUpdates.overwriteUpdateProps (ErrorTestNodeStateProperty(exn))
        |> publishMessage

    TestPrinters.silent
    |> TestPrinters.withBeforeEach beforeEach
    |> TestPrinters.withPassed passed
    |> TestPrinters.withIgnored ignored
    |> TestPrinters.withFailed failed
    |> TestPrinters.withExn exn


type ExpectoExtension () = 

    let testAssembly = System.Reflection.Assembly.GetEntryAssembly()

    interface IExtension with 
        member _.DisplayName: string = "Expecto"
        member _.Description: string = "Expecto test adapter for Microsoft Testing Platform, direct support not VSTestBridge"
        member _.Uid: string = "Set this later other extensions are setting it to nameof(extensionType), but I need to differentiate from the YoloDev extension"
        member _.Version: string = AssemblyVersion.getAssemblyVersion ()
        member _.IsEnabledAsync(): Task<bool> = Task.FromResult(true)

    interface Messages.IDataProducer with 
        member this.DataTypesProduced : Type[] = [| 
            typeof<Messages.TestNodeUpdateMessage>
        |] 
    
    interface ITestFramework with        
        member this.CloseTestSessionAsync(context: CloseTestSessionContext): Task<CloseTestSessionResult> = 
            let result = CloseTestSessionResult(IsSuccess = true)
            Task.FromResult(result)

        member this.CreateTestSessionAsync(context: CreateTestSessionContext): Task<CreateTestSessionResult> = 
            let result = CreateTestSessionResult(IsSuccess = true)
            Task.FromResult(result)

        member this.ExecuteRequestAsync(context: ExecuteRequestContext): Task = 
            let nameSeparator = defaultConfig.joinWith.asString 
            
            let maybeFilter (mtpFilter: ITestExecutionFilter) (expectoTest: Test) = 
                let maybeExpectoFilter = mtpFilter |> Filter.ofMTPFilter nameSeparator |> Result.defaultWith raise
                
                match maybeExpectoFilter with
                | Some filter -> expectoTest |> Test.filter nameSeparator filter
                | None -> expectoTest

            task {
                do! match context.Request with 
                    | :? DiscoverTestExecutionRequest as discoveryRequest ->
                        let maybeTopLevelTest = Expecto.Impl.testFromAssembly testAssembly
                        match maybeTopLevelTest with 
                        | None -> Task.CompletedTask
                        | Some topLevelTest ->
                            let filtered = maybeFilter discoveryRequest.Filter topLevelTest

                            let updateMessages = 
                                filtered 
                                |> TestNodeUpdates.ofExpectoTest nameSeparator (SourceLocation.tryGetCodeLocation testAssembly) discoveryRequest.Session.SessionUid [Messages.DiscoveredTestNodeStateProperty.CachedInstance]
                             
                            updateMessages 
                            |> List.map (fun updateMessage ->
                                context.MessageBus.PublishAsync(this, updateMessage)
                            )
                            |> Array.ofList
                            |> Task.WhenAll

                    | :? RunTestExecutionRequest as runRequest ->
                        let maybeTopLevelTest = Expecto.Impl.testFromAssembly testAssembly
                        match maybeTopLevelTest with 
                        | None -> Task.CompletedTask
                        | Some topLevelTest -> 
                            let filtered = maybeFilter runRequest.Filter topLevelTest
                            let updateMessages = 
                                filtered 
                                |> TestNodeUpdates.ofExpectoTest nameSeparator (SourceLocation.tryGetCodeLocation testAssembly) runRequest.Session.SessionUid []
                            let publishMessage (update: Microsoft.Testing.Platform.Extensions.Messages.TestNodeUpdateMessage) =
                                context.MessageBus.PublishAsync(this, update)
                                |> Async.AwaitTask
                            let printAdapter = MTPPrinterAdapter.create updateMessages publishMessage
                            let config = [CLIArguments.Printer printAdapter]
                            runTestsWithCLIArgsAndCancel context.CancellationToken config [||] filtered |> ignore
                            
                            Task.CompletedTask
                    | _ -> Task.CompletedTask
                    
                context.Complete()
            } 
  

    interface IDisposable with        
        member this.Dispose(): unit = 
            ()

    

type internal ExpectoFrameworkCapabilities () =
    interface ITestFrameworkCapabilities with
      override _.Capabilities : IReadOnlyCollection<ITestFrameworkCapability> = []

open System.Runtime.CompilerServices

[<Extension>]
type TestApplicationBuilderExtensions() =
    
    [<Extension>]
    static member AddExpecto (builder: ITestApplicationBuilder) =
        let capabilityFactory (serviceProvider: IServiceProvider) : ITestFrameworkCapabilities =  
            ExpectoFrameworkCapabilities() 

        let frameworkFactory (capabilities: ITestFrameworkCapabilities) (services: IServiceProvider) : ITestFramework =
            new ExpectoExtension() 
        builder.RegisterTestFramework(capabilityFactory, frameworkFactory)

[<EntryPoint>]
let main argv =
    task {
        let! builder = TestApplication.CreateBuilderAsync(argv) 
        let! testApplication =
            builder
                .AddExpecto()
                .BuildAsync()

        return! testApplication.RunAsync()
    } 
    |> Async.AwaitTask
    |> Async.RunSynchronously

    // Tests.runTestsInAssemblyWithCLIArgs [] argv
