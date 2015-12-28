namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("aws-s3-cache")>]
[<assembly: AssemblyProductAttribute("Exira.AwsS3Cache")>]
[<assembly: AssemblyDescriptionAttribute("Exira.AwsS3Cache is an IIS module which reads cached objects from AWS S3 and quickly serves them to end users")>]
[<assembly: AssemblyVersionAttribute("0.1.19")>]
[<assembly: AssemblyFileVersionAttribute("0.1.19")>]
[<assembly: AssemblyMetadataAttribute("githash","34becc74bacded95cf716e5dfc96b2e6527377d4")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.1.19"
