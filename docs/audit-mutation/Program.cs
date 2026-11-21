using AppWrap.Core;
using AppWrap.Generators;
try { InnoSetupGenerator.Literal("folder{#1+1}"); Console.Error.WriteLine("FAIL: literal preprocessor injection accepted"); return 1; }
catch (UserInputException) { Console.WriteLine("PASS: literal preprocessor injection rejected"); return 0; }
