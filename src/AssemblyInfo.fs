namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("aws-s3-cache")>]
[<assembly: AssemblyProductAttribute("Exira.AwsS3Cache")>]
[<assembly: AssemblyDescriptionAttribute("Exira.AwsS3Cache is IIS module which reads cached objects from AWS S3 and quickly serves them to end users")>]
[<assembly: AssemblyVersionAttribute("0.1.3")>]
[<assembly: AssemblyFileVersionAttribute("0.1.3")>]
[<assembly: AssemblyMetadataAttribute("githash","e0f9b941b33d40fe83f86aa2289a134139bdbe4f")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.1.3"
