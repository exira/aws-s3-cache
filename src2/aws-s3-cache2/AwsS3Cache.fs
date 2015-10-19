namespace AwsS3Cache

open System
open System.Web
open System.Text
open System.Security.Cryptography
open FSharp.Configuration
open Exira.ErrorHandling
open Amazon.S3
open Amazon
open Amazon.S3.Model
open System.IO
open Newtonsoft.Json

type CacheConfig = YamlConfig<"Cache.yaml">

type HeaderObject() =
    member val name = "" with get, set
    member val value = "" with get, set

type Error =
    | InvalidExtension of string // Invalid extension
    | ByPassKeyProvided // Bypass key provided
    | DoNotCache // Should not be cached
    | BlobExpired // Delete blob
    | NotModified // Send 403

type CacheRequest = {
    Start: DateTime
    Application: HttpApplication
    Url: string option
    Hash: string option
}

type CacheResult = {
    Start: DateTime
    Application: HttpApplication
    Url: string
    Hash: string
    LastModified: DateTime
    Content: string
    Headers: HeaderObject list option
}

type AwsS3Cache() =
    let cacheConfig = CacheConfig()

    let mutable _start = DateTime.MinValue

    let (|DateTime|_|) value =
        match DateTime.TryParse value with
        | true, dt -> Some(dt)
        | _ -> None

    let getMD5 (s: string) =
        use md5Obj = new MD5CryptoServiceProvider()

        md5Obj.ComputeHash(Encoding.ASCII.GetBytes(s))
        |> Array.map (fun (x: byte) -> String.Format("{0:x2}", x))
        |> String.concat String.Empty

    // Only execute if the request is for a PHP file
    let checkExtension (data: CacheRequest) =
        match data.Application.Context.Request.Url.GetLeftPart(UriPartial.Path) with
        | leftPart when leftPart.EndsWith(cacheConfig.Cache.Extension) -> succeed data
        | extension -> fail (InvalidExtension extension)

    // Test for Cache Loader
    let checkCacheByPass (data: CacheRequest) =
        match data.Application.Context.Request.UserAgent.ToLowerInvariant() with
        | userAgent when userAgent.Contains(cacheConfig.Cache.BypassKey) -> fail ByPassKeyProvided
        | _ -> succeed data

    // Test for Not Cached
    let checkNoCache (data: CacheRequest) =
        let url = data.Application.Context.Request.Url.ToString().ToLower()

        cacheConfig.Cache.NoCache
        |> Seq.exists (fun x -> url.Contains(x))
        |>  function
            | true -> fail DoNotCache
            | false -> succeed data

    // Construct URL in the same manner as the Project Nami (WordPress) Plugin
    let constructHash (data: CacheRequest) =
        let scheme =
            match data.Application.Context.Request.IsSecureConnection with
            | true -> "https://"
            | false -> "http://"

        let httpHost = data.Application.Context.Request.ServerVariables.["HTTP_HOST"]
        let requestUri = data.Application.Context.Request.ServerVariables.["REQUEST_URI"]
        let url = sprintf "%s%s%s" scheme httpHost requestUri
        let url = Uri(url).GetLeftPart(UriPartial.Query)

        // If Project Nami (WordPress) thinks this is a mobile device, salt the URL to generate a different key
        let mobileSuffix userAgent =
            let containsMobileUserAgent (userAgent: string)  =
                cacheConfig.Cache.MobileUserAgents |> Seq.exists (fun x -> userAgent.Contains(x))

            match userAgent with
            | null -> String.Empty
            | userAgent when containsMobileUserAgent userAgent-> "|mobile"
            | _ -> String.Empty

        let userAgent = data.Application.Context.Request.ServerVariables.["HTTP_USER_AGENT"]
        let url = sprintf "%s%s" url (mobileSuffix userAgent)

        // Generate key based on the URL via MD5 hash
        succeed { data with Url = Some url; Hash =  Some (getMD5 url) }

    // Check cookies and abort if either user is logged in or the Project Nami (WordPress) Plugin has set a commenter cookie on this user for this page
    let checkCookies (data: CacheRequest) =
        let commentKey = sprintf "comment_post_key_%s" data.Hash.Value
        let cookies = data.Application.Context.Request.Cookies

        let checkLoggedInOrComment() =
            data.Application.Context.Request.Cookies.AllKeys
            |> Array.exists (fun x ->
                let key = x.ToLower()
                key.Contains("wordpress_logged_in") || key.Contains(commentKey))

        match cookies with
        | null -> succeed data
        | _ when checkLoggedInOrComment() -> fail DoNotCache
        | _ -> succeed data

    let processCache (data: CacheRequest) =
        // TODO: Check if everything exists
        let bucketName = "bucketName"
        let keyName = "keyName"
        use client = new AmazonS3Client()
        let request = GetObjectRequest(BucketName = bucketName, Key = keyName)
        use response = client.GetObject request

        // Check the TTL of the blob and delete it if it has expired
        let checkExpiration (response: GetObjectResponse) =
            let expirationInSeconds = response.Metadata.["CacheDuration"] |> float
            match response.LastModified.AddSeconds(expirationInSeconds) with
            | expiration when expiration < DateTime.UtcNow -> fail BlobExpired
            | _ -> succeed response

        // Check Last Modified
        let checkLastModified (response: GetObjectResponse) =
            let modifiedSince = data.Application.Request.Headers.["If-Modified-Since"]

            match modifiedSince with
            | null -> succeed response
            | DateTime dt when dt.ToUniversalTime() >= response.LastModified -> fail NotModified
            | _ -> succeed response

        // If we've gotten this far, then we both have something to serve from cache and need to serve it, so get it from blob storage
        let checkContent (response: GetObjectResponse) =
            use reader = new StreamReader(response.ResponseStream)
            let content = reader.ReadToEnd()

            // If the blob is empty, delete it
            if content.Trim().Length = 0 then fail BlobExpired
            else succeed (response, content)

        let checkMetaData (response: GetObjectResponse, content: string) =
            let cacheResult = {
                CacheResult.Start = data.Start
                Hash = data.Hash.Value
                Url = data.Url.Value
                Application = data.Application
                LastModified = response.LastModified
                Content = content
                Headers = None
            }

            if response.Metadata.Keys.Contains("Headers") then
                succeed { cacheResult with Headers = JsonConvert.DeserializeObject<List<HeaderObject>>(response.Metadata.["Headers"]) |> Seq.toList |> Some }
            else
                succeed cacheResult

        let deleteBlob() =
            let deleteRequest = DeleteObjectRequest(BucketName = bucketName, Key = keyName);
            client.DeleteObject deleteRequest

        let cacheResult =
            response
            |> checkExpiration
            |> bind checkLastModified
            |> bind checkContent
            |> bind checkMetaData

        match cacheResult with
        | Failure Error.BlobExpired ->
            deleteBlob() |> ignore
            cacheResult
        | _ -> cacheResult

    let addDebug (data: CacheResult) =
        if cacheConfig.Cache.Debug then
            let cacheStart = data.Start - _start
            let cacheEnd = DateTime.Now - _start

            // Insert debug data before the closing HEAD tag
            let nl = Environment.NewLine
            let rewrite = data.Application.Context.Request.Url.GetLeftPart(UriPartial.Query)
            let debug =
                sprintf
                    "<!-- CacheStart %f CacheEnd %f -->%s<!-- Key %s ServerVar %s Rewrite %s -->%s</head>"
                    cacheStart.TotalMilliseconds
                    cacheEnd.TotalMilliseconds
                    nl
                    data.Hash
                    data.Url
                    rewrite
                    nl

            let content = data.Content.Replace("</head>", debug)

            succeed { data with Content = content }
        else
            succeed data

    let buildResponse (data: CacheResult) =
        // Set last-modified
        data.Application.Context.Response.Cache.SetLastModified data.LastModified

        // Set cache control max age to match remaining cache duration
        // Dim CacheRemaining As TimeSpan = LastModified.UtcDateTime.AddSeconds(ThisBlob.Metadata("Projectnamicacheduration")) - DateTime.UtcNow
        // app.Context.Response.Cache.SetMaxAge(CacheRemaining)
        data.Application.Context.Response.Cache.SetCacheability HttpCacheability.Public
        data.Application.Context.Response.Cache.SetMaxAge(TimeSpan(0, 5, 0))

        // Check for headers in metadata, write them if they exist
        match data.Headers with
        | Some headers ->
            headers
            |> List.iter (fun header -> data.Application.Context.Response.Headers.Add(header.name, header.value))
        | None -> ()

        // Set 200 status, MIME type, and write the blob contents to the response
        data.Application.Context.Response.StatusCode <- 200
        data.Application.Context.Response.Write data.Content
        data.Application.Context.Response.ContentType <- "text/html"

        succeed data.Application

    let beginRequest source e =
        // Record the startup time of the module
        _start <- DateTime.Now

    let resolveRequestCache (source: obj) e =
        let request = {
            CacheRequest.Start = DateTime.Now
            Application = source :?> HttpApplication
            Url = None
            Hash = None
        }

        try
            let result =
                request
                |> checkExtension
                |> bind checkCacheByPass
                |> bind checkNoCache
                |> bind constructHash
                |> bind checkCookies
                |> bind processCache
                |> bind addDebug
                |> bind buildResponse

            match result with
            | Success app ->
                // Notify IIS we are done and to abort further operations
                app.CompleteRequest()
            | Failure Error.NotModified ->
                // Set 304 status (not modified) and abort
                request.Application.Context.Response.StatusCode <- 304
                request.Application.Context.Response.SuppressContent <- true
                request.Application.CompleteRequest()
            | Failure _ -> ()
        with
        | ex -> ()

    interface IHttpModule with
        member this.Dispose() = ()

        member this.Init(context) =
            let onBeginRequest = new EventHandler(beginRequest)
            let onResolveRequestCache = new EventHandler(resolveRequestCache)

            context.BeginRequest.AddHandler onBeginRequest
            context.ResolveRequestCache.AddHandler onResolveRequestCache