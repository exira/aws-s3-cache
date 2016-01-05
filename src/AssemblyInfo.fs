namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("aws-s3-cache")>]
[<assembly: AssemblyProductAttribute("Exira.AwsS3Cache")>]
[<assembly: AssemblyDescriptionAttribute("Exira.AwsS3Cache is an IIS module which reads cached objects from AWS S3 and quickly serves them to end users")>]
[<assembly: AssemblyVersionAttribute("0.1.20")>]
[<assembly: AssemblyFileVersionAttribute("0.1.20")>]
[<assembly: AssemblyMetadataAttribute("githash","0fd0dcb57aacd2c73aecdbe7003cb3ce960046a4")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.1.20"
