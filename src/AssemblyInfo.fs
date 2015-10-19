namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("aws-s3-cache")>]
[<assembly: AssemblyProductAttribute("Exira.AwsS3Cache")>]
[<assembly: AssemblyDescriptionAttribute("Exira.AwsS3Cache is IIS module which reads cached objects from AWS S3 and quickly serves them to end users")>]
[<assembly: AssemblyVersionAttribute("0.1.5")>]
[<assembly: AssemblyFileVersionAttribute("0.1.5")>]
[<assembly: AssemblyMetadataAttribute("githash","99b0936ede0097d6838c25cf9853240790ea2f15")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.1.5"
