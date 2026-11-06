namespace Expecto.MTP.Tests

module MTPWrapper = 
  open System.Diagnostics
  open System.IO
  open System
  open Expecto.MTP.Tests.Ctrf

  let discover (projectPath: string) = 
    // NOTE: --list-tests json doesn't work with `dotnet test` you have to run the test project directly
    let psi = ProcessStartInfo("dotnet", arguments = [|"run"; "--project"; projectPath; "--list-tests"; "json"; "--no-banner"; "-p"; "SkipPack=True"|])
    
    psi.WorkingDirectory <- projectPath
    psi.RedirectStandardOutput <- true

    let discoveryProcess = Process.Start(psi)
    discoveryProcess.WaitForExit()

    let discoveredJson = discoveryProcess.StandardOutput.ReadToEnd()

    MTPDiscoveryModel.parse discoveredJson

  let runToCtrf (projectDir: string) = 
    let reportPath = Path.Join(projectDir, "TestResults", $"{Guid.NewGuid()}.json")
    let psi = ProcessStartInfo("dotnet", arguments = [|"run"; "--project"; projectDir; "-p"; "SkipPack=True"; "--"; "--report-ctrf"; "--report-ctrf-filename"; reportPath; |])
    
    // NOTE: we have to run from the SampleTestProjects directory because we need the global.json with "runner": "Microsoft.Testing.Platform"
    //       Expecto.MTP.Tests can't safely use MTP as the runner because we need to write these tests in order for stable expecto MTP support to exist.  
    //       After we publish a stable version of Expecto.MTP, we could consider moving the whole solution to use the MTP runner.
    psi.WorkingDirectory <- projectDir

    Process.Start(psi).WaitForExit()
    
    let ctrfResults = CtrfParser.ofFilePath reportPath
    ctrfResults


module Tests =
  open Expecto
  open System.IO

  let sampleProjectsDir = Path.Join(__SOURCE_DIRECTORY__, "SampleTestProjects")

  module Outcomes =
    
    let [<Literal>] passed = "passed" 
    let [<Literal>] skipped = "skipped" 
    let [<Literal>] failed = "failed" 

  [<Tests>]
  let tests =
    testList "Expecto.MTP" [
      testCase "Should discover test cases" <| fun _ ->
        
        let expected = ["samples"; "samples.pass"; "samples.fail"; "samples.skipped"; "samples.nested"; "samples.nested.pass"]

        let targetProjectDir = Path.Join(sampleProjectsDir, "SampleConsumer")
        let discoveryResult = MTPWrapper.discover targetProjectDir

        let actual = discoveryResult.tests |> Array.map _.uid |> List.ofArray
        Expect.equal(actual |> List.sort) (expected |> List.sort) ""


      // TODO: I need a test that errored
      testCase "Should run tests and report outcomes. Only runnable tests are reported, not groups" <| fun _ ->
        
        let expected = [
          ("samples.pass", Outcomes.passed); 
          "samples.fail", Outcomes.failed; 
          "samples.skipped", Outcomes.skipped;  
          "samples.nested.pass", Outcomes.passed
        ]

        let targetProjectDir = Path.Join(sampleProjectsDir, "SampleConsumer")
        let runResult = MTPWrapper.runToCtrf targetProjectDir

        let ctrfToLikeness (t: Ctrf.TestModel) = (t.TestId, t.Status) 
        let actual = runResult.Results.Tests |> Array.map ctrfToLikeness |> List.ofArray
        Expect.equal(actual |> List.sort) (expected |> List.sort) ""
    ]
