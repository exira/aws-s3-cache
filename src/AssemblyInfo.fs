namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("aws-s3-cache")>]
[<assembly: AssemblyProductAttribute("Exira.AwsS3Cache")>]
[<assembly: AssemblyDescriptionAttribute("Exira.AwsS3Cache is an IIS module which reads cached objects from AWS S3 and quickly serves them to end users")>]
[<assembly: AssemblyVersionAttribute("0.1.7")>]
[<assembly: AssemblyFileVersionAttribute("0.1.7")>]
[<assembly: AssemblyMetadataAttribute("githash","79dbebbdbfbc6451020bb66693acf1104a371283")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.1.7"
