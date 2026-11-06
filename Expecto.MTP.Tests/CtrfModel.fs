namespace Expecto.MTP.Tests.Ctrf

open System

// SOURCE: https://github.com/ctrf-io/dotnet-ctrf-json-reporter/blob/main/TestResultModel.cs

[<CLIMutable>]
type EnvironmentModel = {
    ShardId: string
}

[<CLIMutable>]
type ToolModel = {
    Name: string
}

[<CLIMutable>]
type SummaryModel = {
    Tests: int
    Passed: int
    Failed: int
    Pending: int
    Skipped: int
    Other: int
    Start: int64
    Stop: int64
}

type TestExtra = {
    ExceptionType: string | null
}

[<CLIMutable>]
type TestModel = {
    TestId: string
    ExecutionId: string
    Name: string
    Status: string
    Duration: int64
    Start: Nullable<int64>
    Stop: Nullable<int64>
    Suite: string[] | null
    Message: string | null
    Trace: string | null
    Line: Nullable<int>
    RawStatus: string | null
    FilePath: string | null
    Stdout: string[] | null
    
    Extra: TestExtra
}

[<CLIMutable>]
type ResultsModel = {
    Environment: EnvironmentModel | null
    Tool: ToolModel
    Summary: SummaryModel
    Tests: TestModel array
}

[<CLIMutable>]
type TestResultsModel = {
    SpecVersion: string
    RunId: string | null
    ReportId: string
    Timestamp: string
    ReportFormat: string
    GeneratedBy: string
    Results: ResultsModel
}


module CtrfParser =
    open System.Text.Json
    open System.IO
    let ofString (ctrfJson: string) : TestResultsModel =
        let opts = JsonSerializerOptions()
        opts.PropertyNameCaseInsensitive <- true
        JsonSerializer.Deserialize<TestResultsModel>(ctrfJson, opts)

    let ofFilePath (path: string) : TestResultsModel = 
        File.ReadAllText(path)
        |> ofString

