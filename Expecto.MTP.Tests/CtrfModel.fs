namespace Expecto.MTP.Tests.Ctrf

open System

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

[<CLIMutable>]
type TestModel = {
    TestId: string
    ExecutionId: string
    Name: string
    Status: string
    Duration: int64
    //[<JsonProperty(NullValueHandling = NullValueHandling.Ignore)>]
    Start: Nullable<int64>
    //[<JsonProperty(NullValueHandling = NullValueHandling.Ignore)>]
    Stop: Nullable<int64>
    //[<JsonProperty(NullValueHandling = NullValueHandling.Ignore)>]
    Suite: string[] | null
    //[<JsonProperty(NullValueHandling = NullValueHandling.Ignore)>]
    Message: string | null
    //[<JsonProperty(NullValueHandling = NullValueHandling.Ignore)>]
    Trace: string | null
    //[<JsonProperty(NullValueHandling = NullValueHandling.Ignore)>]
    Line: Nullable<int>
    //[<JsonProperty(NullValueHandling = NullValueHandling.Ignore)>]
    RawStatus: string | null
    //[<JsonProperty(NullValueHandling = NullValueHandling.Ignore)>]
    FilePath: string | null
    //[<JsonProperty(NullValueHandling = NullValueHandling.Ignore)>]
    Stdout: string[] | null
}

[<CLIMutable>]
type ResultsModel = {
    //[<JsonProperty(NullValueHandling = NullValueHandling.Ignore)>]
    Environment: EnvironmentModel | null
    Tool: ToolModel
    Summary: SummaryModel
    Tests: TestModel array
}

[<CLIMutable>]
type TestResultsModel = {
    SpecVersion: string
    //[<JsonProperty(NullValueHandling = NullValueHandling.Ignore)>]
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

