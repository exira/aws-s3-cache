Imports System
Imports System.Text
Imports System.Web
Imports System.IO
Imports System.Net
Imports System.Security.Cryptography
Imports System.Configuration.ConfigurationManager
Imports System.Linq
Imports Microsoft.WindowsAzure
Imports Microsoft.WindowsAzure.Storage
Imports Microsoft.WindowsAzure.Storage.Blob
Imports Newtonsoft.Json

Public Class CacheFrontEnd
    Implements IHttpModule

    Dim StartDT As DateTime = DateTime.MinValue 'Start time of module
    Dim CacheStartDT As DateTime = DateTime.MinValue 'Start time of cache operations
    Dim CacheEndDT As DateTime = DateTime.MinValue 'End time of cache operations
    Dim ContentTypeFound As Boolean = False 'Track if Content-Type was found in headers

    Public Sub OnBeginRequest(ByVal s As Object, ByVal e As EventArgs)
        'Record the startup time of the module
        StartDT = DateTime.Now
    End Sub

    Public Sub OnCacheRequest(ByVal s As Object, ByVal e As EventArgs)
        Dim app As HttpApplication = CType(s, HttpApplication)

        Try
            'Record the start time of cache operations
            CacheStartDT = DateTime.Now

            'Attempt to access the blob
            Try
                ThisBlob = ThisContainer.GetBlockBlobReference(MD5Hash)
            Catch ex As Exception
                Exit Sub
            End Try

            'Fetch metadata for the blob.  If fails, the blob is not present
            Try
                ThisBlob.FetchAttributes()
            Catch ex As Exception
                Exit Sub
            End Try


            'Determine if Proactive mode is enabled
            If Not IsNothing(System.Configuration.ConfigurationManager.AppSettings("ProjectNamiBlobCache.Proactive")) Then
                If System.Configuration.ConfigurationManager.AppSettings("ProjectNamiBlobCache.Proactive") = "1" Then
                    'Determine if the blob will expire within the next 20% of its total TTL
                    If LastModified.UtcDateTime.AddSeconds(ThisBlob.Metadata("Projectnamicacheduration")) < DateTime.UtcNow.AddSeconds(ThisBlob.Metadata("Projectnamicacheduration") * 0.2) Then 'Extend the cache duration and let the current request through
                        'Update blob metadata to reset the LastModifiedUtc and allow this request through in an attempt to reseed the cache
                        ThisBlob.Metadata("Projectnamicacheduration") = ThisBlob.Metadata("Projectnamicacheduration") - 1
                        ThisBlob.SetMetadata()
                        Exit Sub
                    End If
                End If
            End If



            'If the blob is empty, delete it
            If CacheString.Trim.Length = 0 Then
                ThisBlob.Delete()
                Exit Sub
            End If

            'Record the end time of cache operations
            CacheEndDT = DateTime.Now

            'Determine if Debug mode is enabled
            If Not IsNothing(System.Configuration.ConfigurationManager.AppSettings("ProjectNamiBlobCache.Debug")) Then
                If System.Configuration.ConfigurationManager.AppSettings("ProjectNamiBlobCache.Debug") = "1" Then
                    'Calculate the milliseconds spent until cache operations began, and until completion
                    Dim CacheStartTS As TimeSpan = CacheStartDT - StartDT
                    Dim CacheEndTS As TimeSpan = CacheEndDT - StartDT
                    'Insert debug data before the closing HEAD tag
                    CacheString = CacheString.Replace("<" & Chr(47) & "head>", "<!-- CacheStart " & CacheStartTS.TotalMilliseconds & " CacheEnd " & CacheEndTS.TotalMilliseconds & " -->" & vbCrLf & "<!-- Key " & MD5Hash & " ServerVar " & URL & " Rewrite " & app.Context.Request.Url.GetLeftPart(UriPartial.Query) & " -->" & vbCrLf & "<" & Chr(47) & "head>")
                End If
            End If

            'Check for headers in metadata, write them if they exist
            If ThisBlob.Metadata.ContainsKey("Headers") Then
                Dim Headers = JsonConvert.DeserializeObject(Of List(Of HeaderObject))(ThisBlob.Metadata("Headers"))
                For Each ThisHeader As HeaderObject In Headers
                    If ThisHeader.name.ToLower = "content-type" Then
                        app.Context.Response.ContentType = ThisHeader.value
                        ContentTypeFound = True
                    Else
                        app.Context.Response.Headers.Add(ThisHeader.name, ThisHeader.value)
                    End If
                Next
            End If


            app.Context.Response.Write(CacheString)
            If ContentTypeFound = False Then 'Attempt to detect content type if it was not found in the headers
                If app.Context.Request.ServerVariables("REQUEST_URI").ToLower.EndsWith(".xml") Or CacheString.ToLower.StartsWith("<?xml") Then
                    app.Context.Response.ContentType = "application/xml"
                ElseIf app.Context.Request.ServerVariables("REQUEST_URI").ToLower.EndsWith(".json") Or (CacheString.ToLower.StartsWith("{") And CacheString.ToLower.EndsWith("}")) Then
                    app.Context.Response.ContentType = "application/json"
                Else
                    app.Context.Response.ContentType = "text/html"
                End If
            End If

        Catch ex As Exception

        End Try
    End Sub


    Sub HandleCacheMiss(ByRef app As HttpApplication)
        'Set 200 status, MIME type, and write the blob contents to the response
        app.Context.Response.StatusCode = 200
        app.Context.Response.Write("The page you are attempting to access is currently undergoing maintenance. Please try again later.")
        app.Context.Response.ContentType = "text/html"

        'Notify IIS we are done and to abort further operations
        app.CompleteRequest()
    End Sub

End Class

