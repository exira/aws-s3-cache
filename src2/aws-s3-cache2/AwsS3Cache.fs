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
    member val name = "" with get,set
    member val value = "" with get,set

type AwsS3Cache() =
    let cacheConfig = CacheConfig()

    let (|DateTime|_|) str =
        match DateTime.TryParse str with
        | true, dt -> Some(dt)
        | _ -> None

    let (|Int|_|) str =
        match Int32.TryParse str with
        | true, num -> Some(num)
        | _ -> None

    let (|Float|_|) str =
        match Double.TryParse str with
        | true, num -> Some(num)
        | _ -> None

    let getMD5 (s: string) =
        use md5Obj = new MD5CryptoServiceProvider()

        md5Obj.ComputeHash(Encoding.ASCII.GetBytes(s))
        |> Array.map (fun (x: byte) -> String.Format("{0:x2}", x))
        |> String.concat String.Empty

    // Only execute if the request is for a PHP file
    let checkExtension (app: HttpApplication) =
        match app.Context.Request.Url.GetLeftPart(UriPartial.Path) with
        | leftPart when leftPart.EndsWith(cacheConfig.Cache.Extension) -> succeed app
        | _ -> fail "Invalid extension"

    // Test for Cache Loader
    let checkCacheByPass (app: HttpApplication) =
        match app.Context.Request.UserAgent.ToLowerInvariant() with
        | userAgent when userAgent.Contains(cacheConfig.Cache.BypassKey) -> fail "Bypass key provided"
        | _ -> succeed app

    // Test for Not Cached
    let checkNoCache (app: HttpApplication) =
        let url = app.Context.Request.Url.ToString().ToLower()

        cacheConfig.Cache.NoCache
        |> Seq.exists (fun x -> url.Contains(x))
        |>  function
            | true -> fail "Should not be cached"
            | false -> succeed app

    // Construct URL in the same manner as the Project Nami (WordPress) Plugin
    let constructHash (app: HttpApplication) =
        let scheme =
            match app.Context.Request.IsSecureConnection with
            | true -> "https://"
            | false -> "http://"

        let httpHost = app.Context.Request.ServerVariables.["HTTP_HOST"]
        let requestUri = app.Context.Request.ServerVariables.["REQUEST_URI"]
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

        let userAgent = app.Context.Request.ServerVariables.["HTTP_USER_AGENT"]
        let url = sprintf "%s%s" url (mobileSuffix userAgent)

        // Generate key based on the URL via MD5 hash
        succeed (app, getMD5 url)

    // Check cookies and abort if either user is logged in or the Project Nami (WordPress) Plugin has set a commenter cookie on this user for this page
    let checkCookies (app: HttpApplication, md5: string) =
        let commentKey = sprintf "comment_post_key_%s" md5
        let cookies = app.Context.Request.Cookies

        let checkLoggedInOrComment() =
            app.Context.Request.Cookies.AllKeys
            |> Array.exists (fun x ->
                let key = x.ToLower()
                key.Contains("wordpress_logged_in") || key.Contains(commentKey))

        match cookies with
        | null -> succeed (app, md5)
        | _ when checkLoggedInOrComment() -> fail "Should not be cached"
        | _ -> succeed (app, md5)

    let processCache (app: HttpApplication, md5: string) =
        // TODO: Check if everything exists, type the Fail side and deal with it later
        let bucketName = "bucketName"
        let keyName = "keyName"
        use client = new AmazonS3Client(RegionEndpoint.EUCentral1)
        let request = GetObjectRequest(BucketName = bucketName, Key = keyName)
        use response = client.GetObject request

        // Check the TTL of the blob and delete it if it has expired
        let checkExpiration (response: GetObjectResponse) =
            let expirationInSeconds = response.Metadata.["CacheDuration"] |> float
            match response.LastModified.AddSeconds(expirationInSeconds) with
            | expiration when expiration < DateTime.UtcNow -> fail "Delete blob"
            | _ -> succeed response

        // Check Last Modified
        let checkLastModified (response: GetObjectResponse) =
            let modifiedSince = app.Request.Headers.["If-Modified-Since"]

            match modifiedSince with
            | null -> succeed response
            | DateTime dt when dt.ToUniversalTime() >= response.LastModified -> fail "Send 403"
            //'Set 304 status (not modified) and abort
            //app.Context.Response.StatusCode = 304
            //app.Context.Response.SuppressContent = True
            //app.CompleteRequest()
            | _ -> succeed response

        // If we've gotten this far, then we both have something to serve from cache and need to serve it, so get it from blob storage
        let checkContent (response: GetObjectResponse) =
            use reader = new StreamReader(response.ResponseStream)
            succeed (response, reader.ReadToEnd())

        let checkMetaData (response: GetObjectResponse, content: string) =
            if response.Metadata.Keys.Contains("Headers") then
                succeed (app, response.LastModified, content, JsonConvert.DeserializeObject<List<HeaderObject>>(response.Metadata.["Headers"]) |> Seq.toList)
            else
                succeed (app, response.LastModified, content, [])

        response
        |> checkExpiration
        |> bind checkLastModified
        |> bind checkContent
        |> bind checkMetaData

    // TODO: Throw stuff into record types
    let buildResponse (app: HttpApplication, lastModified: DateTime, content: string, headers: HeaderObject list) =
        // Set last-modified
        app.Context.Response.Cache.SetLastModified(lastModified)

        // Set cache control max age to match remaining cache duration
        // Dim CacheRemaining As TimeSpan = LastModified.UtcDateTime.AddSeconds(ThisBlob.Metadata("Projectnamicacheduration")) - DateTime.UtcNow
        // app.Context.Response.Cache.SetMaxAge(CacheRemaining)
        app.Context.Response.Cache.SetCacheability(HttpCacheability.Public)
        app.Context.Response.Cache.SetMaxAge(TimeSpan(0, 5, 0))

        // Set 200 status, MIME type, and write the blob contents to the response
        app.Context.Response.StatusCode <- 200


        succeed app

    let beginRequest source e =
        ()

    let resolveRequestCache (source: obj) e =
        let app = source :?> HttpApplication

        let result =
            app
            |> checkExtension
            |> bind checkCacheByPass
            |> bind checkNoCache
            |> bind constructHash
            |> bind checkCookies
            |> bind processCache
            |> bind buildResponse

        match result with
        | Success app ->
            // Notify IIS we are done and to abort further operations
            app.CompleteRequest()
        | Failure _ -> ()

    interface IHttpModule with
        member this.Dispose() = ()

        member this.Init(context) =
            let onBeginRequest = new EventHandler(beginRequest)
            let onResolveRequestCache = new EventHandler(resolveRequestCache)

            context.BeginRequest.AddHandler onBeginRequest
            context.ResolveRequestCache.AddHandler onResolveRequestCache