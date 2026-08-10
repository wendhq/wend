using Wend.Api;

namespace Wend.Tests;

public class AuthEmailSenderTests
{
    private string _path = null!;

    [SetUp]
    public void SetUp() => _path = Path.Combine(Path.GetTempPath(), $"wend_email_{Guid.NewGuid():N}.log");

    [TearDown]
    public void TearDown() => File.Delete(_path);

    [Test]
    public async Task Sending_writes_the_link_to_the_log_file()
    {
        var sender = new FileAuthEmailSender(_path);

        await sender.SendEmailConfirmationAsync("someone@example.test", "https://wend.test/verify?code=abc");

        var written = await File.ReadAllTextAsync(_path);
        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("someone@example.test"));
            Assert.That(written, Does.Contain("https://wend.test/verify?code=abc"));
        });
    }

    [Test]
    public async Task Sending_twice_appends_rather_than_overwrites()
    {
        var sender = new FileAuthEmailSender(_path);

        await sender.SendEmailConfirmationAsync("first@example.test", "https://wend.test/verify?code=1");
        await sender.SendEmailConfirmationAsync("second@example.test", "https://wend.test/verify?code=2");

        var written = await File.ReadAllTextAsync(_path);
        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("first@example.test"));
            Assert.That(written, Does.Contain("second@example.test"));
        });
    }
}
