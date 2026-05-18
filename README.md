# Expecto Pure MTP Experiment

## Motivation

Expecto currently supports VSTest and Microsoft.Testing.Platform (MTP) through [YoloDev.Expecto.TestSdk](https://github.com/YoloDev/YoloDev.Expecto.TestSdk).

MTP support is accomplished using [VSTestBridge](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-extensions-vstest-bridge).
This enables progressive support, allowing expecto to be discovered by both VSTest and MTP.

However, it also comes with a downside: it cuts us off from MTP features not available in VSTest. Primarily, better test hierarchy handling. I think MTP also supports more specific code location ranges instead of just line number, enabling a smoother experience with location-based features (i.e. go to test, show test in explorer, run test at cursor)


## Exploration

A [key motivation behind MTP](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-intro#mtp-pillars) was eliminating reflection to improve predictability and compatability with .NET framework features like native AOT. 


