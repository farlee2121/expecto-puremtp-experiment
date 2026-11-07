namespace Expecto.MTP

module Filter = 
    open Microsoft.Testing.Platform.Requests
    open Microsoft.Testing.Platform.Extensions
    open System

    let private sequenceResultM (results: Result<'ok, 'error> seq) : Result<'ok seq, 'error> =
        Seq.fold (fun state next -> 
                state |> Result.bind (fun (successes: 'ok ResizeArray) ->
                    match next with
                    | Ok nextSuccess -> 
                        successes.Add(nextSuccess)
                        Ok successes
                    | Error error -> Error error
                )
            ) 
            (Ok (ResizeArray<'ok>())) results
        |> Result.map (fun l -> l :> 'ok seq)
    let ofMTPFilter (joinWith: string) (mtpFilter: ITestExecutionFilter) : Result<(string list -> bool) option, Exception> =
        let rec recurse (mtpFilter: ITestExecutionFilter) : Result<(string list -> bool) option, Exception> = 
            match mtpFilter with 
            | :? NopFilter ->
                Ok None
                // Ok (fun _ -> true)
            | :? TestNodeUidListFilter as uidFilter->
                // PROBLEM: the MTP TestNodeUid is meant to uniquely identify tests groupings or cases, 
                //          but expecto's Test.filter flattens to testLeafs to filter. 
                //          It's possible we could include extra tests who's name hierarchy flattens the same even if the segments are different 
                (Ok << Some)(fun (nameSegments: string list) -> 
                    let flatName = nameSegments |> String.concat joinWith
                    uidFilter.TestNodeUids 
                    |> Array.exists (fun (uid: Messages.TestNodeUid) -> flatName.StartsWith(uid))
                ) 

            // NOTE: --treenode-filter isn't accepted as a valid cli argument. It's experimental, so there may be special steps to enable it. 
            //       I couldn't test the TestNodeFilter and CompositTestExecutionFilter, so I commented them out

            // | :? TreeNodeFilter as nodeFilter ->
            //     (Ok << Some) (fun (nameSegments: string list) -> 
            //         let flatName = nameSegments |> String.concat joinWith
            //         nodeFilter.MatchesFilter(flatName, Messages.PropertyBag())
            //     ) 

            // | :? CompositeTestExecutionFilter as composite-> 
            //     match composite.Operator with
            //     | TestExecutionFilterOperator.And ->
            //         composite.Filters 
            //         |> Seq.map recurse 
            //         |> sequenceResultM 
            //         |> Result.map  (fun subFilters -> 
            //             Some (fun nameSegmentList -> 
            //                 subFilters |> Seq.forall (fun f -> 
            //                     match f with 
            //                     | Some f -> f nameSegmentList
            //                     | None -> true
            //                 )
            //             )
            //         ) 
            //     | unsupportedOperator -> Error (InvalidOperationException($"Unsupported filter operator {unsupportedOperator}"))
            | unsupported -> 
                let filterTypeName = 
                    match unsupported with 
                    | null -> "null"
                    | unsupported -> unsupported.GetType().FullName
                (InvalidOperationException($"Unsupported filter type '{filterTypeName}'")) :> Exception |> Error

        recurse mtpFilter