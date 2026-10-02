using SkoulouApp.Core.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class AnswerMatchingServiceTests
{
    [Fact]
    public void SplitVariants_TrenntRichTextMitParagraphenInEinzelneVarianten()
    {
        var input = "<p>alpha</p><p>beta</p>";

        var result = AnswerMatchingService.SplitVariants(input);

        Assert.Equal(["alpha", "beta"], result);
    }

    [Fact]
    public void SplitVariants_TrenntRichTextMitBrInEinzelneVarianten()
    {
        var input = "alpha<br>beta<br />gamma";

        var result = AnswerMatchingService.SplitVariants(input);

        Assert.Equal(["alpha", "beta", "gamma"], result);
    }

    [Fact]
    public void SplitVariants_DekodiertHtmlEntitiesUndEntferntLeereZeilen()
    {
        var input = "<div>f(x) = a &lt; b</div><div> </div><div>g(x) = c &amp; d</div>";

        var result = AnswerMatchingService.SplitVariants(input);

        Assert.Equal(["f(x) = a < b", "g(x) = c & d"], result);
    }
}
