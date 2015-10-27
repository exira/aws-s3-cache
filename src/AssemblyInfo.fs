namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("aws-s3-cache")>]
[<assembly: AssemblyProductAttribute("Exira.AwsS3Cache")>]
[<assembly: AssemblyDescriptionAttribute("Exira.AwsS3Cache is an IIS module which reads cached objects from AWS S3 and quickly serves them to end users")>]
[<assembly: AssemblyVersionAttribute("0.1.8")>]
[<assembly: AssemblyFileVersionAttribute("0.1.8")>]
[<assembly: AssemblyMetadataAttribute("githash","b494e2e17f4e3a5e2711c42103978edb843de75d")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.1.8"
