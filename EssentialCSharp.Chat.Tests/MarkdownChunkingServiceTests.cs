using EssentialCSharp.Chat.Common.Services;
using Moq;

namespace EssentialCSharp.Chat.Tests;
// TODO: Move to editorconfig later, just moving quick
#pragma warning disable CA1707 // Identifiers should not contain underscores
public class MarkdownChunkingServiceTests
{
    #region MarkdownContentToHeadersAndSection
    [Fact]
    public void MarkdownContentToHeadersAndSection_ParsesSampleMarkdown_CorrectlyCombinesHeadersAndExtractsContent()
    {
        string markdown = """
### Beginner Topic
####  What Is a Method?

Syntactically, a **method** in C# is a named block of code introduced by a method declaration (e.g., `static void Main()`) and (usually) followed by zero or more statements within curly braces. Methods perform computations and/or actions. Like paragraphs in written languages, methods provide a means of structuring and organizing code so that it is more readable. More important, methods can be reused and called from multiple places and so avoid the need to duplicate code. The method declaration introduces the method and defines the method name along with the data passed to and from the method. In Listing 1.8, `Main()` followed by `{ ... }` is an example of a C# method.

##  Main Method

The location where C# programs begin execution is the **Main method**, which begins with `static void Main()`. When you execute the program by typing `dotnet run` on the terminal, the program starts with the Main method and begins executing the first statement, as identified in Listing 1.8.



### Listing 1.8:  Breaking Apart `HelloWorld`
publicclass Program                // BEGIN Class definition
{
publicstaticvoid Main()       // Method declaration
    {                               // BEGIN method implementation
        Console.WriteLine(          // This statement spans 2 lines
"Hello, My name is Inigo Montoya");
    }                               // END method implementation
}                                   // END class definition
Although the Main method declaration can vary to some degree, `static` and the method name, `Main`, are always required for a program (see “Advanced Topic: Declaration of the Main Method”).

The **comments**, text that begins with `//` in Listing 1.8, are explained later in the chapter. They are included to identify the various constructs in the listing.

### Advanced Topic
####  Declaration of the Main Method

C# requires that the Main method return either `void` or `int` and that it take either no parameters or a single array of strings. Listing 1.9 shows the full declaration of the Main method. The `args` parameter is an array of strings corresponding to the command-line arguments. The executable name is not included in the `args` array (unlike in C and C++). To retrieve the full command used to execute the program, including the program name, use `Environment.CommandLine`.
""";

        var sections = MarkdownChunkingService.MarkdownContentToHeadersAndSection(markdown);

        Assert.Equal(3, sections.Count);
        Assert.Contains(sections, s => s.Header == "Beginner Topic: What Is a Method?" && string.Join("\n", s.Content).Contains("Syntactically, a **method** in C# is a named block of code"));
        Assert.Contains(sections, s => s.Header == "Main Method" && string.Join("\n", s.Content).Contains("The location where C# programs begin execution is the **Main method**, which begins with `static void Main()`")
            && string.Join("\n", s.Content).Contains("publicclass Program"));
        Assert.Contains(sections, s => s.Header == "Main Method: Advanced Topic: Declaration of the Main Method" && string.Join("\n", s.Content).Contains("C# requires that the Main method return either `void` or `int`"));
    }

    [Fact]
    public void MarkdownContentToHeadersAndSection_AppendsCodeListingToPriorSection()
    {
        string markdown = """
##  Working with Variables

Now that you’ve been introduced to the most basic C# program, it’s time to declare a local variable. Once a variable is declared, you can assign it a value, replace that value with a new value, and use it in calculations, output, and so on. However, you cannot change the data type of the variable. In Listing 1.12, `string max` is a variable declaration.



### Listing 1.12: Declaring and Assigning a Variable

publicclass MiracleMax
{
publicstaticvoid Main()
    {
string max;     // "string" identifies the data type
// "max" is the variable
        max = "Have fun storming the castle!";
        Console.WriteLine(max);
    }
}

### Beginner Topic
####  Local Variables

A **variable** is a name that refers to a value that can change over time. Local indicates that the programmer **declared** the variable within a method.

To declare a variable is to define it, which you do by

* Specifying the type of data which the variable will contain
* Assigning it an identifier (name)
""";

        var sections = MarkdownChunkingService.MarkdownContentToHeadersAndSection(markdown);

        Assert.Equal(2, sections.Count);
        // The code listing should be appended to the Working with Variables section, not as its own section
        var workingWithVariablesSection = sections.FirstOrDefault(s => s.Header == "Working with Variables");
        Assert.True(!string.IsNullOrEmpty(workingWithVariablesSection.Header));
        Assert.Contains("publicclass MiracleMax", string.Join("\n", workingWithVariablesSection.Content));
        Assert.DoesNotContain(sections, s => s.Header == "Listing 1.12: Declaring and Assigning a Variable");
    }

    [Fact]
    public void MarkdownContentToHeadersAndSection_KeepsPriorHeadersAppended()
    {
        string markdown = """
### Beginner Topic
####  What Is a Data Type?

The type of data that a variable declaration specifies is called a **data type** (or object type). A data type, or simply **type**, is a classification of things that share similar characteristics and behavior. For example, animal is a type. It classifies all things (monkeys, warthogs, and platypuses) that have animal characteristics (multicellular, capacity for locomotion, and so on). Similarly, in programming languages, a type is a definition for several items endowed with similar qualities.

##  Declaring a Variable

In Listing 1.12, `string max` is a variable declaration of a string type whose name is `max`. It is possible to declare multiple variables within the same statement by specifying the data type once and separating each identifier with a comma. Listing 1.13 demonstrates such a declaration.

### Listing 1.13: Declaring Two Variables within One Statement
string message1, message2;

### Declaring another thing

Because a multivariable declaration statement allows developers to provide the data type only once within a declaration, all variables will be of the same type.

In C#, the name of the variable may begin with any letter or an underscore (`_`), followed by any number of letters, numbers, and/or underscores. By convention, however, local variable names are camelCased (the first letter in each word is capitalized, except for the first word) and do not include underscores.

##  Assigning a Variable

After declaring a local variable, you must assign it a value before reading from it. One way to do this is to use the `=` **operator**, also known as the **simple assignment operator**. Operators are symbols used to identify the function the code is to perform. Listing 1.14 demonstrates how to use the assignment operator to designate the string values to which the variables `miracleMax` and `valerie` will point.

### Listing 1.14: Changing the Value of a Variable
publicclass StormingTheCastle
{
publicstaticvoid Main()
    {
string valerie;
string miracleMax = "Have fun storming the castle!";
 
        valerie = "Think it will work?";
 
        Console.WriteLine(miracleMax);
        Console.WriteLine(valerie);
 
        miracleMax = "It would take a miracle.";
        Console.WriteLine(miracleMax);
    }
}

### Continued Learning
From this listing, observe that it is possible to assign a variable as part of the variable declaration (as it was for `miracleMax`) or afterward in a separate statement (as with the variable `valerie`). The value assigned must always be on the right side of the declaration.
""";

        var sections = MarkdownChunkingService.MarkdownContentToHeadersAndSection(markdown);
        Assert.Equal(5, sections.Count);

        Assert.Contains(sections, s => s.Header == "Beginner Topic: What Is a Data Type?" && string.Join("\n", s.Content).Contains("The type of data that a variable declaration specifies is called a **data type**"));
        Assert.Contains(sections, s => s.Header == "Declaring a Variable" && string.Join("\n", s.Content).Contains("In Listing 1.12, `string max` is a variable declaration"));
        Assert.Contains(sections, s => s.Header == "Declaring a Variable: Declaring another thing" && string.Join("\n", s.Content).Contains("Because a multivariable declaration statement allows developers to provide the data type only once"));
        Assert.Contains(sections, s => s.Header == "Assigning a Variable" && string.Join("\n", s.Content).Contains("After declaring a local variable, you must assign it a value before reading from it."));
        Assert.Contains(sections, s => s.Header == "Assigning a Variable: Continued Learning" && string.Join("\n", s.Content).Contains("From this listing, observe that it is possible to assign a variable as part of the variable declaration"));
    }
    #endregion MarkdownContentToHeadersAndSection

    #region ProcessSingleMarkdownFile
    [Fact]
    public void ProcessSingleMarkdownFile_ProducesExpectedChunksAndHeaders()
    {
        // Arrange
        var logger = new Mock<Microsoft.Extensions.Logging.ILogger<MarkdownChunkingService>>().Object;
        var service = new MarkdownChunkingService(logger);
        string[] fileContent = new[]
        {
            "## Section 1",
            "This is the first section.",
            "",
            "### Listing 1.1: Example Listing",
            "Console.WriteLine(\"Hello World\");",
            "",
            "## Section 2",
            "This is the second section."
        };
        string fileName = "TestFile.md";
        string filePath = "/path/to/TestFile.md";

        // Act
        var result = service.ProcessSingleMarkdownFile(fileContent, fileName, filePath);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(fileName, result.FileName);
        Assert.Equal(filePath, result.FilePath);
        Assert.Contains("This is the first section.", string.Join("\n", result.Chunks));
        Assert.Contains("Console.WriteLine(\"Hello World\");", string.Join("\n", result.Chunks));
        Assert.Contains("This is the second section.", string.Join("\n", result.Chunks));
        Assert.Contains(result.Chunks, c => c.Contains("This is the second section."));
    }
    #endregion ProcessSingleMarkdownFile
}

#pragma warning restore CA1707 // Identifiers should not contain underscores
