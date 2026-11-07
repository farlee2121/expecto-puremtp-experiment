namespace Expecto.MTP.Tests.MTPDiscoveryModel


type Location = {
    file: string
    lineStart: int
    lineEnd: int
}


type DiscoveredTest = {
    uid: string
    displayName: string
    location: Location
}

type DiscoverResult = {
    schemaVersion: int
    tests: DiscoveredTest array
}

module DiscoverResult = 
    open System.Text.Json
    let parse (json: string) : DiscoverResult=
        JsonSerializer.Deserialize<DiscoverResult>(json) 