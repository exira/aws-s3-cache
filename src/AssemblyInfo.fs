namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("aws-s3-cache")>]
[<assembly: AssemblyProductAttribute("Exira.AwsS3Cache")>]
[<assembly: AssemblyDescriptionAttribute("Exira.AwsS3Cache is an IIS module which reads cached objects from AWS S3 and quickly serves them to end users")>]
[<assembly: AssemblyVersionAttribute("0.1.15")>]
[<assembly: AssemblyFileVersionAttribute("0.1.15")>]
[<assembly: AssemblyMetadataAttribute("githash","9dd863cc4864069852094f1a0fe0a25cdba65347")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.1.15"
