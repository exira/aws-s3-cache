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
            'Only execute if the request is for a PHP file
            If app.Context.Request.Url.GetLeftPart(UriPartial.Path).EndsWith(".php") Then


                'Record the start time of cache operations
                CacheStartDT = DateTime.Now

                'Set up connection to the cache
                Dim ThisStorageAccount As CloudStorageAccount = CloudStorageAccount.Parse("DefaultEndpointsProtocol=http;AccountName=" & System.Configuration.ConfigurationManager.AppSettings("ProjectNamiBlobCache.StorageAccount") & ";AccountKey=" & System.Configuration.ConfigurationManager.AppSettings("ProjectNamiBlobCache.StorageKey"))
                Dim ThisBlobClient As CloudBlobClient = ThisStorageAccount.CreateCloudBlobClient
                Dim ThisContainer As CloudBlobContainer = ThisBlobClient.GetContainerReference(System.Configuration.ConfigurationManager.AppSettings("ProjectNamiBlobCache.StorageContainer"))
                Dim ThisBlob As CloudBlockBlob

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

                'Check the TTL of the blob and delete it if it has expired
                Dim LastModified As DateTimeOffset = ThisBlob.Properties.LastModified
                If LastModified.UtcDateTime.AddSeconds(ThisBlob.Metadata("Projectnamicacheduration")) < DateTime.UtcNow Then 'Cache has expired
                    ThisBlob.Delete()
                    Exit Sub
                Else
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

                    'Check Last Modified
                    If Not IsNothing(app.Request.Headers("If-Modified-Since")) Then
                        Dim ClientLastModified As DateTime
                        If DateTime.TryParse(app.Request.Headers("If-Modified-Since"), ClientLastModified) Then
                            If ClientLastModified.ToUniversalTime >= LastModified.UtcDateTime Then
                                'Set 304 status (not modified) and abort
                                app.Context.Response.StatusCode = 304
                                app.Context.Response.SuppressContent = True
                                app.CompleteRequest()
                                Exit Sub
                            End If
                        End If
                    End If

                    'If we've gotten this far, then we both have something to serve from cache and need to serve it, so get it from blob storage
                    Dim CacheString As String = ThisBlob.DownloadText()

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

                    'Set last-modified
                    app.Context.Response.Cache.SetLastModified(LastModified.UtcDateTime)

                    'Set cache control max age to match remaining cache duration
                    Dim CacheRemaining As TimeSpan = LastModified.UtcDateTime.AddSeconds(ThisBlob.Metadata("Projectnamicacheduration")) - DateTime.UtcNow
                    app.Context.Response.Cache.SetCacheability(HttpCacheability.Public)
                    'app.Context.Response.Cache.SetMaxAge(CacheRemaining)
                    app.Context.Response.Cache.SetMaxAge(New TimeSpan(0, 5, 0))

                    'Set 200 status, MIME type, and write the blob contents to the response
                    app.Context.Response.StatusCode = 200
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

                    'Notify IIS we are done and to abort further operations
                    app.CompleteRequest()
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

    Public Class HeaderObject
        Public Property name() As String
            Get
                Return m_name
            End Get
            Set(value As String)
                m_name = value
            End Set
        End Property
        Private m_name As String = ""

        Public Property value() As String
            Get
                Return m_value
            End Get
            Set(value As String)
                m_value = value
            End Set
        End Property
        Private m_value As String = ""
    End Class
End Class

