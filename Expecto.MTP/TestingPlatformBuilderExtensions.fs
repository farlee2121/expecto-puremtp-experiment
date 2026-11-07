namespace Expecto.MTP

open System.Runtime.CompilerServices
open Microsoft.Testing.Platform.Builder
open System
open Microsoft.Testing.Platform.Capabilities.TestFramework
open Microsoft.Testing.Platform.Extensions.TestFramework

[<Extension>]
type TestApplicationBuilderExtensions() =
    
    [<Extension>]
    static member AddExpecto (builder: ITestApplicationBuilder) =
        let capabilityFactory (serviceProvider: IServiceProvider) : ITestFrameworkCapabilities =  
            TestFrameworkCapabilities()

        let frameworkFactory (capabilities: ITestFrameworkCapabilities) (services: IServiceProvider) : ITestFramework =
            new ExpectoExtension() 
        builder.RegisterTestFramework(capabilityFactory, frameworkFactory)
