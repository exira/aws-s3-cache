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

    let getMD5 (s: string) =
        use md5Obj = new MD5CryptoServiceProvider()

        md5Obj.ComputeHash(Encoding.ASCII.GetBytes(s))
        |> Array.map (fun (x : byte) -> String.Format("{0:x2}", x))
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
        | null -> succeed app
        | _ when checkLoggedInOrComment() -> fail "Should not be cached"
        | _ -> succeed app

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