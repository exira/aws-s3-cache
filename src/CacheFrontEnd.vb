Public Sub OnCacheRequest(ByVal s As Object, ByVal e As EventArgs)
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
End Sub

