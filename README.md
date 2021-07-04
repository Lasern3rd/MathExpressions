# MathExpressions
Fast and simple math expression evaluation.

Compiles expressions into IL code using System.Reflection.Emit.DynamicMethod.

## Usage
```cs
var xpr = MathExpression.Compile("x + pow(y, 3) * x");
double d = xpr.Evaluate(5d, 2d);
// d == 45d
d = xpr.Evaluate(2d, 3d);
// d == 56d
```

## Features
- Constants (Math.E as e, Math.PI as pi) and numeric literals ([0-9][.0-9]*)
- Variables (alphanumeric, first character must be a letter, i.e. [a-zA-Z][a-zA-Z0-9]*)
- Functions (abs, log, max, min, pow, sqrt)
