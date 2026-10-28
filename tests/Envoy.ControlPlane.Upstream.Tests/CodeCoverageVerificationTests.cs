using System.Xml.Linq;
using Envoy.ControlPlane.Tools;
using Xunit;

namespace Envoy.ControlPlane.Upstream.Tests;

public sealed class CodeCoverageVerificationTests
{
    [Theory]
    [InlineData("Envoy.ControlPlane")]
    [InlineData("Envoy.ControlPlane.AspNetCore")]
    [InlineData("Envoy.ControlPlane.Validation")]
    public void ReportWithoutMeasuredLibrarySource_IsRejected(string missingPackage)
    {
        var report = XElement.Parse("""
            <coverage><packages>
              <package name="Envoy.ControlPlane"><classes><class><lines><line number="1" hits="1" /></lines></class></classes></package>
              <package name="Envoy.ControlPlane.AspNetCore"><classes><class><lines><line number="2" hits="1" /></lines></class></classes></package>
              <package name="Envoy.ControlPlane.Validation"><classes><class><lines><line number="3" hits="1" /></lines></class></classes></package>
            </packages></coverage>
            """);
        report.Element("packages")!.Elements("package")
            .Single(item => (string?)item.Attribute("name") == missingPackage)
            .Descendants("line").Single().Remove();

        var error = Assert.Throws<InvalidOperationException>(() => CodeCoverageVerification.VerifyLibraryCoverage(report));
        Assert.Contains(missingPackage, error.Message);
    }
}
