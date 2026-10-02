using Amazon.Runtime;
using Amazon.S3;
using FluentAssertions;
using Pena_e_Arte.Infrastructure.Extensions;
using Xunit;

namespace Pena_e_Arte.UnitTests.Services;

public class R2S3ConfigTests
{
    [Fact]
    public void CreateR2S3Config_OnlyAddsChecksumsWhenRequired_BecauseR2RejectsTrailerChecksums()
    {
        AmazonS3Config config = InfrastructureServiceExtensions.CreateR2S3Config("acct123");

        config.RequestChecksumCalculation.Should().Be(RequestChecksumCalculation.WHEN_REQUIRED);
        config.ResponseChecksumValidation.Should().Be(ResponseChecksumValidation.WHEN_REQUIRED);
    }

    [Fact]
    public void CreateR2S3Config_TargetsAccountEndpointWithPathStyle()
    {
        AmazonS3Config config = InfrastructureServiceExtensions.CreateR2S3Config("acct123");

        config.ServiceURL.Should().Be("https://acct123.r2.cloudflarestorage.com/");
        config.ForcePathStyle.Should().BeTrue();
    }
}
