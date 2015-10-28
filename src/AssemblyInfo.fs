namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("aws-s3-cache")>]
[<assembly: AssemblyProductAttribute("Exira.AwsS3Cache")>]
[<assembly: AssemblyDescriptionAttribute("Exira.AwsS3Cache is an IIS module which reads cached objects from AWS S3 and quickly serves them to end users")>]
[<assembly: AssemblyVersionAttribute("0.1.17")>]
[<assembly: AssemblyFileVersionAttribute("0.1.17")>]
[<assembly: AssemblyMetadataAttribute("githash","3bee5b75d983937bda281a37e71e684c253dbd34")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.1.17"
