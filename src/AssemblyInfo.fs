namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("aws-s3-cache")>]
[<assembly: AssemblyProductAttribute("Exira.AwsS3Cache")>]
[<assembly: AssemblyDescriptionAttribute("Exira.AwsS3Cache is an IIS module which reads cached objects from AWS S3 and quickly serves them to end users")>]
[<assembly: AssemblyVersionAttribute("0.1.18")>]
[<assembly: AssemblyFileVersionAttribute("0.1.18")>]
[<assembly: AssemblyMetadataAttribute("githash","c1a9b287138e77d8d79fae6b86c099797326b069")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.1.18"
