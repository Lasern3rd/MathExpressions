using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace MathExpressions
{
    public class MathExpression
    {
        private static readonly Dictionary<string, MethodInfo> Functions = new Dictionary<string, MethodInfo>()
        {
            { "abs", typeof(Math).GetMethod("Abs", new Type[] { typeof(double) }) },
            { "log", typeof(Math).GetMethod("Log", new Type[] { typeof(double) }) },
            { "max", typeof(Math).GetMethod("Max", new Type[] { typeof(double), typeof(double) }) },
            { "min", typeof(Math).GetMethod("Min", new Type[] { typeof(double), typeof(double) }) },
            { "pow", typeof(Math).GetMethod("Pow", new Type[] { typeof(double), typeof(double) })},
            { "sqrt", typeof(Math).GetMethod("Sqrt", new Type[] { typeof(double) }) }
        };

        private DynamicMethod dynamicMethod;

        public static MathExpression Compile(string xpr, string name = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                byte[] rnd = new byte[10];
                new Random().NextBytes(rnd);
                name = "MathExpression_" + Convert.ToBase64String(rnd);
            }

            // tokenize
            Tuple<List<MathExpressionToken>, int> tokensAndArgCount = Tokenizer.Tokenize(xpr);

            List<MathExpressionToken> tokens = tokensAndArgCount.Item1;
            int argCount = tokensAndArgCount.Item2;

            Type[] parameterTypes = new Type[argCount];
            Array.Fill(parameterTypes, typeof(double));

            DynamicMethod dynamicMethod = new DynamicMethod(name, typeof(double),
                parameterTypes, typeof(MathExpression).Module);

            ILGenerator generator = dynamicMethod.GetILGenerator();

            // shunting yard
            Dictionary<string, int> args = new Dictionary<string, int>();
            Stack<MathExpressionToken> ops = new Stack<MathExpressionToken>();
            MathExpressionToken op;

            foreach (MathExpressionToken token in tokens)
            {
                switch (token.type)
                {
                    case MathExpressionTokenType.Constant:
                        generator.Emit(OpCodes.Ldc_R8, double.Parse(token.value));
                        break;

                    case MathExpressionTokenType.Variable:
                        if (token.value == "e")
                        {
                            generator.Emit(OpCodes.Ldc_R8, Math.E);
                            break;
                        }
                        if (token.value == "pi")
                        {
                            generator.Emit(OpCodes.Ldc_R8, Math.PI);
                            break;
                        }
                        if (!args.TryGetValue(token.value, out int argIndex))
                        {
                            args[token.value] = argIndex = args.Count;
                        }
                        switch (argIndex)
                        {
                            case 0:
                                generator.Emit(OpCodes.Ldarg_0);
                                break;
                            case 1:
                                generator.Emit(OpCodes.Ldarg_1);
                                break;
                            case 2:
                                generator.Emit(OpCodes.Ldarg_2);
                                break;
                            case 3:
                                generator.Emit(OpCodes.Ldarg_3);
                                break;
                            default:
                                generator.Emit(OpCodes.Ldarg, argIndex);
                                break;
                        }
                        break;

                    case MathExpressionTokenType.Function:
                        ops.Push(token);
                        break;

                    case MathExpressionTokenType.Operator:
                        while (ops.Count > 0)
                        {
                            op = ops.Peek();
                            if (op.type == MathExpressionTokenType.LBrace || op.Precedence < token.Precedence)
                            {
                                break;
                            }
                            ops.Pop();
                            op.EmitForOperatorOrFunction(generator);
                        }
                        ops.Push(token);
                        break;

                    case MathExpressionTokenType.LBrace:
                        ops.Push(token);
                        break;

                    case MathExpressionTokenType.RBrace:
                        while (true)
                        {
                            if (ops.Count == 0)
                            {
                                throw new ArgumentException("Unbalanced parentheses.");
                            }
                            op = ops.Pop();
                            if (op.type == MathExpressionTokenType.LBrace)
                            {
                                break;
                            }
                            op.EmitForOperatorOrFunction(generator);
                        }
                        if (ops.Count > 0 && ops.Peek().type == MathExpressionTokenType.Function)
                        {
                            ops.Pop().EmitForOperatorOrFunction(generator);
                        }
                        break;
                }
            }

            while (ops.Count > 0)
            {
                op = ops.Pop();
                if (op.type == MathExpressionTokenType.LBrace)
                {
                    throw new ArgumentException("Unbalanced parentheses.");
                }
                op.EmitForOperatorOrFunction(generator);
            }

            generator.Emit(OpCodes.Ret);

            return new MathExpression() { dynamicMethod = dynamicMethod };
        }

        public double Evaluate(params double[] args)
        {
            return (double)dynamicMethod.Invoke(null, args.Select(d => (object)d).ToArray());
        }

        private struct MathExpressionToken
        {
            public readonly MathExpressionTokenType type;
            public readonly string value;

            public MathExpressionToken(MathExpressionTokenType type, string value)
            {
                this.type = type;
                this.value = value;
            }

            public int Precedence
            {
                get
                {
                    switch (value)
                    {
                        case "+":
                        case "-":
                            return 2;
                        case "*":
                        case "/":
                            return 3;

                        default:
                            throw new ArgumentException("Unknown operator '" + value + "'.");
                    }
                }
            }

            public void EmitForOperatorOrFunction(ILGenerator generator)
            {
                switch (value)
                {
                    case "+":
                        generator.Emit(OpCodes.Add);
                        break;

                    case "-":
                        generator.Emit(OpCodes.Sub);
                        break;

                    case "*":
                        generator.Emit(OpCodes.Mul);
                        break;

                    case "/":
                        generator.Emit(OpCodes.Div);
                        break;

                    default:
                        if (!Functions.TryGetValue(value, out MethodInfo function)) {
                            throw new ArgumentException("Unknown operator or function '" + value + "'.");
                        }
                        generator.Emit(OpCodes.Call, function);
                        break;
                }
            }
        }

        private enum MathExpressionTokenType
        {
            Constant, Variable, Function, Operator, LBrace, RBrace
        }

        private class Tokenizer
        {
            private int pos;
            private string xpr;
            private ISet<string> variables;

            public static Tuple<List<MathExpressionToken>, int> Tokenize(string xpr)
            {
                Tokenizer tokenizer = new Tokenizer()
                {
                    pos = 0,
                    xpr = xpr,
                    variables = new HashSet<string>()
                };

                return new Tuple<List<MathExpressionToken>, int>(tokenizer.Parse(),
                    tokenizer.variables.Count);
            }

            private List<MathExpressionToken> Parse()
            {
                List<MathExpressionToken> tokens = new List<MathExpressionToken>();

                while (pos < xpr.Length)
                {
                    switch (xpr[pos])
                    {
                        case ' ':
                        case '\t':
                        case ',':
                            ++pos;
                            break;

                        case '(':
                            tokens.Add(new MathExpressionToken(MathExpressionTokenType.LBrace, null));
                            ++pos;
                            break;

                        case ')':
                            tokens.Add(new MathExpressionToken(MathExpressionTokenType.RBrace, null));
                            ++pos;
                            break;

                        case '+':
                        case '-':
                        case '*':
                        case '/':
                            tokens.Add(new MathExpressionToken(MathExpressionTokenType.Operator, xpr[pos].ToString()));
                            ++pos;
                            break;

                        case char c when '0' <= c && c <= '9':
                            tokens.Add(ParseConstant());
                            break;

                        case char c when IsAlpha(c):
                            tokens.Add(ParseVariableOrFunction());
                            break;

                        default:
                            throw new ArgumentException();
                    }
                }

                return tokens;
            }

            private MathExpressionToken ParseVariableOrFunction()
            {
                int start = pos;
                while (++pos < xpr.Length && IsAlphaOrDigit(xpr[pos])) ;

                string tkn = xpr[start..pos];

                if (IsFunction(tkn))
                {
                    return new MathExpressionToken(MathExpressionTokenType.Function, tkn);
                }
                variables.Add(tkn);
                return new MathExpressionToken(MathExpressionTokenType.Variable, tkn);
            }

            private bool IsFunction(string tkn)
            {
                return Functions.ContainsKey(tkn);
            }

            private MathExpressionToken ParseConstant()
            {
                int start = pos;
                while (++pos < xpr.Length && IsNumber(xpr[pos])) ;

                return new MathExpressionToken(
                    MathExpressionTokenType.Constant, xpr[start..pos]);
            }

            private static bool IsAlphaOrDigit(char c)
            {
                return IsAlpha(c) || IsDigit(c);
            }

            private static bool IsNumber(char c)
            {
                return IsDigit(c) || c == '.';
            }

            private static bool IsDigit(char c)
            {
                return '0' <= c && c <= '9';
            }

            private static bool IsAlpha(char c)
            {
                return ('A' <= c && c <= 'Z') || ('a' <= c && c <= 'z');
            }
        }
    }
}
