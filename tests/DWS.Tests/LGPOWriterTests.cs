using System.Text;
using DWS.Core.Policy;
using Xunit;

namespace DWS.Tests;

public class LGPOWriterTests
{
    [Fact]
    public void LGPOWriter_SetsDwordAndStringPolicies_AndGeneratesValidPRegBinary()
    {
        var writer = new LGPOWriter();
        writer.SetDwordPolicy(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0);
        writer.SetStringPolicy(@"SOFTWARE\Policies\Microsoft\Windows\Explorer", "BannerText", "Restricted");

        Assert.Equal(2, writer.EntryCount);

        string tempPol = Path.Combine(Path.GetTempPath(), $"dws_test_{Guid.NewGuid():N}.pol");
        try
        {
            writer.SavePolicy(tempPol);

            Assert.True(File.Exists(tempPol));
            byte[] bytes = File.ReadAllBytes(tempPol);

            // Verify PReg Header (Signature: 0x67655250 'PReg', Version: 1)
            uint signature = BitConverter.ToUInt32(bytes, 0);
            uint version = BitConverter.ToUInt32(bytes, 4);

            Assert.Equal(0x67655250u, signature); // 'PReg'
            Assert.Equal(1u, version);

            // Verify UTF-16LE delimiters: Entry 1 starts with '[' (0x005B -> 0x5B, 0x00)
            Assert.Equal(0x5B, bytes[8]);
            Assert.Equal(0x00, bytes[9]);

            // Convert remaining bytes to Unicode string to verify key text is present
            string unicodeContent = Encoding.Unicode.GetString(bytes);
            Assert.Contains(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", unicodeContent);
            Assert.Contains("AllowTelemetry", unicodeContent);
            Assert.Contains(@"SOFTWARE\Policies\Microsoft\Windows\Explorer", unicodeContent);
            Assert.Contains("BannerText", unicodeContent);
        }
        finally
        {
            if (File.Exists(tempPol))
            {
                File.Delete(tempPol);
            }
        }
    }
}
