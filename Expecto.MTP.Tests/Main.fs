namespace Expecto.MTP.Tests

open Expecto

module Program =
    [<EntryPoint>]
    let main argv =
        Tests.runTestsInAssemblyWithCLIArgs [] argv