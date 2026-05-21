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

module MTPTestNode =
    open Microsoft.Testing.Platform.Extensions.Messages
    open Microsoft.Testing.Platform.TestHost

    let newTestNode (uid: TestNodeUid) (displayName: string) (properties: PropertyBag) = 
        TestNode(Uid = uid, DisplayName = displayName, Properties = properties)

    let ofExpectoTest (sessionId: SessionUid) (test: Test) : TestNodeUpdateMessage list =
        let rec recurse (parentId: TestNodeUid option) (test: Test) : TestNodeUpdateMessage list =
            match test with 
            | Test.Sequenced (_, childTest) -> recurse parentId childTest
            | Test.TestList (tests, _) -> tests |> List.collect (recurse parentId)
            | Test.TestCase (testcode, _) -> []
            | Test.TestLabel (label, childTest, _) -> 
                let currentNode =
                    let parentUidString = parentId |> Option.map _.Value |> Option.defaultValue String.Empty
                    // NOTE: the TestNodeUid is expected to be stable across test runs
                    let testUid = TestNodeUid(parentUidString + "." + label)
                    TestNodeUpdateMessage(
                        sessionId,
                        new TestNode(Uid = testUid, DisplayName = label, Properties = PropertyBag(DiscoveredTestNodeStateProperty.CachedInstance)),
                        parentId |> Option.defaultValue null
                    )
                let childNodes = recurse (Some currentNode.TestNode.Uid) childTest
                currentNode :: childNodes

        recurse None test


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
            let result = CloseTestSessionResult()
            result.IsSuccess <- true
            Task.FromResult(result)

        member this.CreateTestSessionAsync(context: CreateTestSessionContext): Task<CreateTestSessionResult> = 
            let result = CreateTestSessionResult()
            result.IsSuccess <- true
            Task.FromResult(result)

        member this.ExecuteRequestAsync(context: ExecuteRequestContext): Task = 
            match context.Request with 
            | :? DiscoverTestExecutionRequest as discoveryRequest -> 
                let maybeTopLevelTest = Expecto.Impl.testFromAssembly testAssembly
                match maybeTopLevelTest with 
                | None -> Task.CompletedTask
                | Some topLevelTest ->
                    let updateMessages = topLevelTest |> MTPTestNode.ofExpectoTest discoveryRequest.Session.SessionUid 
                    updateMessages 
                    |> List.map (fun updateMessage ->
                        context.MessageBus.PublishAsync(this, updateMessage)
                    )
                    |> Array.ofList
                    |> Task.WhenAll
            | :? RunTestExecutionRequest as runRequest -> 
                raise (NotImplementedException())
            | _ -> Task.CompletedTask

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
