namespace Expecto.MTP.Tests

module MTPWrapper = 
  open System.Diagnostics
  open System.IO
  open System
  open Expecto.MTP.Tests.Ctrf
  open Expecto.MTP.Tests.MTPDiscoveryModel

  let discoverWithFilter (projectPath: string) (filter: string option) = 
    // NOTE: --list-tests json doesn't work with `dotnet test` you have to run the test project directly
    let psi = ProcessStartInfo("dotnet", arguments = [|
      "run"; "--project"; projectPath; 
      "-p"; "SkipPack=True"
      "--"
      "--list-tests"; "json";
      "--no-banner"; 
      match filter with | Some filter -> yield! [|"--filter-uid"; filter |] | None -> ()
    |])
    
    psi.WorkingDirectory <- projectPath
    psi.RedirectStandardOutput <- true

    let discoveryProcess = Process.Start(psi)
    discoveryProcess.WaitForExit()

    let discoveredJson = discoveryProcess.StandardOutput.ReadToEnd()

    DiscoverResult.parse discoveredJson

  let discover (projectPath: string) = discoverWithFilter projectPath None

  let runToCtrfWithFilter (projectDir: string) (filter: string option) = 
    let reportPath = Path.Join(projectDir, "TestResults", $"{Guid.NewGuid()}.json")
    let psi = ProcessStartInfo("dotnet", arguments = [|
      "run"; "--project"; projectDir; 
      "-p"; "SkipPack=True"; 
      "--"; 
      "--report-ctrf"; "--report-ctrf-filename"; reportPath;
      match filter with | Some filter -> yield! [|"--filter-uid"; filter |] | None -> () 
    |])
    
    // NOTE: we have to run from the SampleTestProjects directory because we need the global.json with "runner": "Microsoft.Testing.Platform"
    //       Expecto.MTP.Tests can't safely use MTP as the runner because we need to write these tests in order for stable expecto MTP support to exist.  
    //       After we publish a stable version of Expecto.MTP, we could consider moving the whole solution to use the MTP runner.
    psi.WorkingDirectory <- projectDir

    Process.Start(psi).WaitForExit()
    
    let ctrfResults = CtrfParser.ofFilePath reportPath
    ctrfResults

  let runToCtrf (projectPath: string) = runToCtrfWithFilter projectPath None


module Tests =
  open Expecto
  open System.IO


  module Outcomes =
    
    let [<Literal>] passed = "passed" 
    let [<Literal>] skipped = "skipped" 
    let [<Literal>] failed = "failed" 


  module SampleProjects = 
    let sampleProjectsDir = Path.Join(__SOURCE_DIRECTORY__, "SampleTestProjects")

    let sampleConsumer = Path.Join(sampleProjectsDir, "SampleConsumer")

  open Expecto.MTP.Tests.MTPDiscoveryModel

  [<Tests>]
  let tests =
    testList "Expecto.MTP" [
      testList "discover" [
        testCase "Should discover the expected test cases" <| fun _ ->
        
          let expected = [
            "samples"
            "samples.pass" 
            "samples.fail"
            "samples.error"
            "samples.skipped" 
            "samples.nested" 
            "samples.nested.pass"
            "samples.ignored"
          ]

          let discoveryResult = MTPWrapper.discover SampleProjects.sampleConsumer

          let actual = discoveryResult.tests |> Array.map _.uid |> List.ofArray
          Expect.equal(actual |> List.sort) (expected |> List.sort) ""

        testCase "should show justs the tests in a group when filtered to the groups uid" <| fun _ ->
          
          let groupId = "samples.nested"
          let expected = [ 
            "samples.nested.pass"
          ]

          let discoveryResult = MTPWrapper.discoverWithFilter SampleProjects.sampleConsumer (Some groupId)

          let actual = discoveryResult.tests |> Array.map _.uid |> List.ofArray
          Expect.equal(actual |> List.sort) (expected |> List.sort) ""

        testCase "should show just the specified test when filtered to a test's uid" <| fun _ ->
          
          let testId= "samples.nested.pass"
          let expected = [ testId ]

          let discoveryResult = MTPWrapper.discoverWithFilter SampleProjects.sampleConsumer (Some testId)

          let actual = discoveryResult.tests |> Array.map _.uid |> List.ofArray
          Expect.equal(actual) (expected) ""

        testCase "should include test source locations" <| fun () -> 
          let testId= "samples.pass"
          let expected : DiscoveredTest array = 
            [| 
              { uid = testId; displayName = "pass"; location = { file = Path.Join( SampleProjects.sampleConsumer, "Sample.fs"); lineStart = 12; lineEnd = 12 } }
            |]

          let discoveryResult = MTPWrapper.discoverWithFilter SampleProjects.sampleConsumer (Some testId)

          // MTP returns the uid in the display name when you filter, but just the test's name if you list all tests, so mapping to exclude the displayName 
          let toLikeness (t: DiscoveredTest) = (t.uid, t.location) 
          let actual = discoveryResult.tests
          Expect.equal(actual |> Array.map toLikeness) (expected |> Array.map toLikeness) ""
      ]
      
      testList "run" [
        
        testCase "Should run tests and report outcomes. Only runnable tests are reported, not groups" <| fun _ ->
          let expected = [
            ("samples.pass", Outcomes.passed) 
            "samples.fail", Outcomes.failed
            "samples.skipped", Outcomes.skipped 
            "samples.nested.pass", Outcomes.passed
            "samples.error", Outcomes.failed
            "samples.ignored", Outcomes.skipped
          ]

          let runResult = MTPWrapper.runToCtrf SampleProjects.sampleConsumer

          let ctrfToLikeness (t: Ctrf.TestModel) = (t.TestId, t.Status) 
          let actual = runResult.Results.Tests |> Array.map ctrfToLikeness |> List.ofArray
          Expect.equal(actual |> List.sort) (expected |> List.sort) ""

        testCase "Should include trace information on errored tests" <| fun _ ->
          let runResult = MTPWrapper.runToCtrf SampleProjects.sampleConsumer

          let actualErroredTest = runResult.Results.Tests |> Array.tryFind (fun t -> t.TestId = "samples.error")

          Expect.isSome actualErroredTest ""
          Expect.isNotEmpty actualErroredTest.Value.Trace ""
          Expect.equal 
            (actualErroredTest.Value.Message, actualErroredTest.Value.Extra.ExceptionType)
            ("Special error message", "Tests+SpecialException") 
            ""

        
        testCase "Should report test duration for any non-skipped tests" <| fun _ -> 
          let targetProjectDir = SampleProjects.sampleConsumer
          let runResult = MTPWrapper.runToCtrf targetProjectDir

          let actual = runResult.Results.Tests |> Array.filter (fun t -> t.Status <> Outcomes.skipped)
          
          Expect.isNonEmpty actual ""
          Expect.isTrue (actual |> Array.forall (fun t -> t.Duration > 0)) $"Actual: {actual}"


        testCase "should execute just the tests in a group when filtered to the groups uid" <| fun _ ->        
          let groupId = "samples.nested"
          let expected = [
            "samples.nested.pass", Outcomes.passed
          ]

          let runResult = MTPWrapper.runToCtrfWithFilter SampleProjects.sampleConsumer (Some groupId)

          let ctrfToLikeness (t: Ctrf.TestModel) = (t.TestId, t.Status) 
          let actual = runResult.Results.Tests |> Array.map ctrfToLikeness |> List.ofArray
          Expect.equal(actual |> List.sort) (expected |> List.sort) ""

        testCase "should execute just the request test when filtered to a test's id" <| fun _ ->        
          let testId = "samples.fail"
          let expected = [
            testId, Outcomes.failed
          ]

          let runResult = MTPWrapper.runToCtrfWithFilter SampleProjects.sampleConsumer (Some testId)

          let ctrfToLikeness (t: Ctrf.TestModel) = (t.TestId, t.Status) 
          let actual = runResult.Results.Tests |> Array.map ctrfToLikeness |> List.ofArray
          Expect.equal(actual |> List.sort) (expected |> List.sort) ""

        // PROBLEM: CTRF doesn't include test location data, so we'll have to use a different report format to test this
        // testCase "run should report test locations" <| fun () -> 
        //   ()
      ]
      

    ]
