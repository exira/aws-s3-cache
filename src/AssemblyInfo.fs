namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("aws-s3-cache")>]
[<assembly: AssemblyProductAttribute("Exira.AwsS3Cache")>]
[<assembly: AssemblyDescriptionAttribute("Exira.AwsS3Cache is an IIS module which reads cached objects from AWS S3 and quickly serves them to end users")>]
[<assembly: AssemblyVersionAttribute("0.1.6")>]
[<assembly: AssemblyFileVersionAttribute("0.1.6")>]
[<assembly: AssemblyMetadataAttribute("githash","cf09ec993c11c205a936603c793fafa6bf1a463d")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.1.6"
