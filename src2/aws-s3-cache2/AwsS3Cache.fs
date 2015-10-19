namespace AwsS3Cache

open System
open System.Web
open System.Text
open System.Security.Cryptography
open FSharp.Configuration
open Exira.ErrorHandling

type CacheConfig = YamlConfig<"Cache.yaml">

type AwsS3Cache() =
    let cacheConfig = CacheConfig()

    // Only execute if the request is for a PHP file
    let checkExtension (app: HttpApplication) =
        match app.Context.Request.Url.GetLeftPart(UriPartial.Path).EndsWith(".php") with
        | true -> succeed app
        | false -> fail "Invalid extension"

    // Test for Cache Loader
    let checkCacheByPass (app: HttpApplication) =
        let byPassKey = cacheConfig.Cache.BypassKey.ToLowerInvariant()
        let userAgent = app.Context.Request.UserAgent.ToLowerInvariant()

        match userAgent.Contains(byPassKey) with
        | true -> fail "Bypass key provided"
        | false -> succeed app

    // Test for Not Cached
    let checkNoCache (app: HttpApplication) =
        let url = app.Context.Request.Url.ToString().ToLower()

        // List of URL parts which will never be cached
        let noCache = [|
            "/wp-admin/"
            "/wp-login.php"
            "/wp-cron.php"
            "/wp-comments-post.php"
            "/xmlrpc.php"
            "/wp-signup.php"
            "/wp-trackback.php"
            "/wp-links-opml.php"
            "/wp-blog-header.php"
        |]

        let containsNoCache =
            noCache |> Array.exists (fun x -> url.Contains(x))

        match containsNoCache with
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
        let userAgent = app.Context.Request.ServerVariables.["HTTP_USER_AGENT"]

        // If Project Nami (WordPress) thinks this is a mobile device, salt the URL to generate a different key
        let mobileSuffix (userAgent: string) =
            // List of mobile User Agent lookups from Project Nami (WordPress) vars.php
            let mobileAgents = [|
                "Mobile"
                "Android"
                "Silk/"
                "Kindle"
                "BlackBerry"
                "Opera Mini"
                "Opera Mobi"
            |]

            match userAgent with
            | null -> String.Empty
            | _ when (mobileAgents |> Array.exists (fun x -> userAgent.Contains(x))) -> "|mobile"
            | _ -> String.Empty

        let url = sprintf "%s%s%s" scheme httpHost requestUri
        let url = Uri(url).GetLeftPart(UriPartial.Query)
        let url = sprintf "%s%s" url (mobileSuffix userAgent)

        let getMD5 (s: string) =
            let md5Obj = new MD5CryptoServiceProvider()

            md5Obj.ComputeHash(Encoding.ASCII.GetBytes(s))
            |> Array.map (fun (x : byte) -> System.String.Format("{0:X2}", x))
            |> String.concat System.String.Empty

        // Generate key based on the URL via MD5 hash
        let md5 = getMD5 url

        succeed (app, md5)

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
        | null -> succeed app
        | _ ->
            match checkLoggedInOrComment() with
            | false -> succeed app
            | true -> fail "Should not be cached"

    let beginRequest source e =
        ()

    let resolveRequestCache (source: obj) e =
        let app = source :?> HttpApplication

        let r =
            app
            |> checkExtension
            |> bind checkCacheByPass
            |> bind checkNoCache
            |> bind constructHash
            |> bind checkCookies
        ()

    interface IHttpModule with
        member this.Dispose() = ()

        member this.Init(context) =
            let onBeginRequest = new EventHandler(beginRequest)
            let onResolveRequestCache = new EventHandler(resolveRequestCache)

            context.BeginRequest.AddHandler onBeginRequest
            context.ResolveRequestCache.AddHandler onResolveRequestCache