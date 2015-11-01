﻿// Prototyping extended expression trees for C#.
//
// bartde - October 2015

using Microsoft.CSharp.Expressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using static Tests.ReflectionUtils;
using static Tests.TestHelpers;

namespace Tests
{
    [TestClass]
    public class InvocationTests
    {
        [TestMethod]
        public void Invoke_Factory_ArgumentChecking()
        {
            // NB: A lot of checks are performed by LINQ helpers, so we omit tests for those cases.

            var invoke = MethodInfoOf((Func<int, int, int> f) => f.Invoke(default(int), default(int)));

            var parameters = invoke.GetParameters();

            var arg1Parameter = parameters[0];
            var arg2Parameter = parameters[1];

            var arg1 = Expression.Constant(0);
            var arg2 = Expression.Constant(1);

            var argArg1 = CSharpExpression.Bind(arg1Parameter, arg1);
            var argArg2 = CSharpExpression.Bind(arg2Parameter, arg2);

            var cout = MethodInfoOf(() => Console.WriteLine(default(int)));

            var valueParameter = cout.GetParameters()[0];

            var value = Expression.Constant(42);
            var argValue = CSharpExpression.Bind(valueParameter, value);

            var function = Expression.Constant(new Func<int, int, int>((x, y) => x + y));

            // duplicate
            AssertEx.Throws<ArgumentException>(() => CSharpExpression.Invoke(function, argArg1, argArg1));

            // unbound
            AssertEx.Throws<ArgumentException>(() => CSharpExpression.Invoke(function, argArg1));

            // wrong member
            AssertEx.Throws<ArgumentException>(() => CSharpExpression.Invoke(function, argValue));

            // null
            var bindings = new[] { argArg1, argArg2 };
            AssertEx.Throws<ArgumentNullException>(() => CSharpExpression.Invoke(default(Expression), bindings));
            AssertEx.Throws<ArgumentNullException>(() => CSharpExpression.Invoke(default(Expression), bindings.AsEnumerable()));
        }

        [TestMethod]
        public void Invoke_Properties()
        {
            var invoke = MethodInfoOf((Func<int, int, int> f) => f.Invoke(default(int), default(int)));

            var parameters = invoke.GetParameters();

            var arg1Parameter = parameters[0];
            var arg2Parameter = parameters[1];

            var arg1Value = Expression.Constant(0);
            var arg2Value = Expression.Constant(1);

            var arg0 = CSharpExpression.Bind(arg1Parameter, arg1Value);
            var arg1 = CSharpExpression.Bind(arg2Parameter, arg2Value);

            var function = Expression.Constant(new Func<int, int, int>((x, y) => x + y));

            {
                var res = CSharpExpression.Invoke(function, arg0, arg1);

                Assert.AreEqual(CSharpExpressionType.Invoke, res.CSharpNodeType);
                Assert.AreSame(function, res.Expression);
                Assert.AreEqual(typeof(int), res.Type);
                Assert.IsTrue(res.Arguments.SequenceEqual(new[] { arg0, arg1 }));
            }

            {
                var res = CSharpExpression.Invoke(function, new[] { arg0, arg1 }.AsEnumerable());

                Assert.AreEqual(CSharpExpressionType.Invoke, res.CSharpNodeType);
                Assert.AreSame(function, res.Expression);
                Assert.AreEqual(typeof(int), res.Type);
                Assert.IsTrue(res.Arguments.SequenceEqual(new[] { arg0, arg1 }));
            }
        }

        [TestMethod]
        public void Invoke_Update()
        {
            var invoke = MethodInfoOf((Func<int, int, int> f) => f.Invoke(default(int), default(int)));

            var parameters = invoke.GetParameters();

            var arg1Parameter = parameters[0];
            var arg2Parameter = parameters[1];

            var arg1Value = Expression.Constant(0);
            var arg2Value = Expression.Constant(1);

            var arg0 = CSharpExpression.Bind(arg1Parameter, arg1Value);
            var arg1 = CSharpExpression.Bind(arg2Parameter, arg2Value);

            var function = Expression.Constant(new Func<int, int, int>((x, y) => x + y));

            var res = CSharpExpression.Invoke(function, arg0, arg1);

            var function1 = Expression.Constant(new Func<int, int, int>((x, y) => x + y));

            var upd1 = res.Update(function1, res.Arguments);
            Assert.AreNotSame(upd1, res);
            Assert.AreSame(res.Arguments, upd1.Arguments);
            Assert.AreSame(function1, upd1.Expression);

            var upd2 = res.Update(function, new[] { arg1, arg0 });
            Assert.AreNotSame(upd2, res);
            Assert.AreSame(res.Expression, upd2.Expression);
            Assert.IsTrue(upd2.Arguments.SequenceEqual(new[] { arg1, arg0 }));
        }

        [TestMethod]
        public void Invoke_Compile1()
        {
            var invoke = MethodInfoOf((Func<int, int, int> f) => f.Invoke(default(int), default(int)));

            var parameters = invoke.GetParameters();

            var parameterArg1 = parameters[0];
            var parameterArg2 = parameters[1];

            var valueArg1 = Expression.Constant(1);
            var valueArg2 = Expression.Constant(2);

            var function = Expression.Constant(new Func<int, int, int>((x, y) => x + y));

            AssertCompile<int>(log =>
                CSharpExpression.Invoke(log(function, "F"),
                    CSharpExpression.Bind(parameterArg1, log(valueArg1, "1")),
                    CSharpExpression.Bind(parameterArg2, log(valueArg2, "2"))
                ),
                new LogAndResult<int> { Value = 1 + 2, Log = { "F", "1", "2" } }
            );

            AssertCompile<int>(log =>
                CSharpExpression.Invoke(log(function, "F"),
                    CSharpExpression.Bind(parameterArg2, log(valueArg2, "2")),
                    CSharpExpression.Bind(parameterArg1, log(valueArg1, "1"))
                ),
                new LogAndResult<int> { Value = 1 + 2, Log = { "F", "2", "1" } }
            );
        }

        [TestMethod]
        public void Invoke_Compile2()
        {
            var invoke = MethodInfoOf((D f) => f.Invoke(default(int), default(int)));

            var parameters = invoke.GetParameters();

            var parameterArg1 = parameters[0];
            var parameterArg2 = parameters[1];

            var valueArg1 = Expression.Constant(1);
            var valueArg2 = Expression.Constant(2);

            var function = Expression.Constant(new D((x, y) => x + y));

            AssertCompile<int>(log =>
                CSharpExpression.Invoke(log(function, "F"),
                    CSharpExpression.Bind(parameterArg1, log(valueArg1, "1")),
                    CSharpExpression.Bind(parameterArg2, log(valueArg2, "2"))
                ),
                new LogAndResult<int> { Value = 1 + 2, Log = { "F", "1", "2" } }
            );

            AssertCompile<int>(log =>
                CSharpExpression.Invoke(log(function, "F"),
                    CSharpExpression.Bind(parameterArg1, log(valueArg1, "1"))
                ),
                new LogAndResult<int> { Value = 1 + 42, Log = { "F", "1" } }
            );
        }

        private void AssertCompile<T>(Func<Func<Expression, string, Expression>, Expression> createExpression, LogAndResult<T> expected)
        {
            var res = WithLogValue<T>(createExpression).Compile()();
            Assert.AreEqual(expected, res);
        }

        [TestMethod]
        public void Invoke_Visitor()
        {
            var invoke = MethodInfoOf((Func<int, int, int> f) => f.Invoke(default(int), default(int)));

            var parameters = invoke.GetParameters();

            var parameterArg1 = parameters[0];
            var parameterArg2 = parameters[1];

            var valueArg1 = Expression.Constant(1);
            var valueArg2 = Expression.Constant(2);

            var function = Expression.Constant(new Func<int, int, int>((x, y) => x + y));

            var res = CSharpExpression.Invoke(function, CSharpExpression.Bind(parameterArg1, valueArg1), CSharpExpression.Bind(parameterArg2, valueArg2));

            var v = new V();
            Assert.AreSame(res, v.Visit(res));
            Assert.IsTrue(v.Visited);
        }

        class V : CSharpExpressionVisitor
        {
            public bool Visited = false;

            protected override Expression VisitInvocation(InvocationCSharpExpression node)
            {
                Visited = true;

                return base.VisitInvocation(node);
            }
        }

        delegate int D(int arg1, int arg2 = 42);
    }
}
