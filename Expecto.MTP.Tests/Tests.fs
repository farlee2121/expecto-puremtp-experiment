namespace Expecto.MTP.Tests

module MTPWrapper = 
  open System.Diagnostics
  let discover (projectPath: string) = 
    // NOTE: --list-tests json doesn't work with `dotnet test` you have to run the test project directly
    let psi = ProcessStartInfo("dotnet", arguments = [|"run"; "--project"; projectPath; "--list-tests"; "json"; "--no-banner"; "-p"; "SkipPack=True"|])
    
    psi.WorkingDirectory <- projectPath
    psi.RedirectStandardOutput <- true

    let discoveryProcess = Process.Start(psi)
    discoveryProcess.WaitForExit()

    let discoveredJson = discoveryProcess.StandardOutput.ReadToEnd()

    MTPDiscoveryModel.parse discoveredJson

module Tests =
  open Expecto
  open System.IO

  let sampleProjectsDir = Path.Join(__SOURCE_DIRECTORY__, "SampleTestProjects")

  [<Tests>]
  let tests =
    testList "Expecto.MTP" [
      testCase "Should discover test cases" <| fun _ ->
        
        let expected = ["samples"; "samples.pass"; "samples.fail"; "samples.skipped"; "samples.nested"; "samples.nested.pass"]

        let targetProjectDir = Path.Join(sampleProjectsDir, "SampleConsumer")
        let discoveryResult = MTPWrapper.discover targetProjectDir

        let actual = discoveryResult.tests |> Array.map _.uid |> List.ofArray
        Expect.equal(actual |> List.sort) (expected |> List.sort) ""
    ]
