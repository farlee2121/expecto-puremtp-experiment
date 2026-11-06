namespace Expecto.MTP.Tests

module MTPDiscoveryModel = 

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

  open System.Text.Json
  let parse (json: string) : DiscoverResult=
    JsonSerializer.Deserialize<DiscoverResult>(json) 