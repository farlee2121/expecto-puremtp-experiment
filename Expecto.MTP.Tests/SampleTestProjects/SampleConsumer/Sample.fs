module Tests

open Expecto

[<Tests>]
let tests =
  testList "samples" [
    testCase "pass" <| fun _ ->
      let subject = true
      Expect.isTrue subject "I compute, therefore I am."

    testCase "fail" <| fun _ ->
      let subject = false
      Expect.isTrue subject "I should fail because the subject is false"

    testCase "skipped" <| fun _ ->
      Tests.skiptest "Yup, waiting for a sunny day..."

    testList "nested" [
      testCase "pass" <| fun () -> ()
    ]
  ]
