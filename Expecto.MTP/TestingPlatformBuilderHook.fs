namespace Expecto.MTP
open Microsoft.Testing.Platform.Builder

[<AbstractClass;Sealed>]
type TestingPlatformBuilderHook =
    static member AddExtensions(testApplicationBuilder: ITestApplicationBuilder, arguments: string[]) : unit =
        testApplicationBuilder.AddExpecto() |> ignore