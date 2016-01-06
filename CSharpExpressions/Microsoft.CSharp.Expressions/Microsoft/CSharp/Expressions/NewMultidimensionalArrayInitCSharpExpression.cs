﻿// Prototyping extended expression trees for C#.
//
// bartde - October 2015

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic.Utils;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using static Microsoft.CSharp.Expressions.Helpers;
using static System.Linq.Expressions.ExpressionStubs;
using LinqError = System.Linq.Expressions.Error;

namespace Microsoft.CSharp.Expressions
{
    /// <summary>
    /// Represents creating a new multi-dimensional array and possibly initializing the elements of the new array.
    /// </summary>
    public sealed partial class NewMultidimensionalArrayInitCSharpExpression : CSharpExpression
    {
        internal NewMultidimensionalArrayInitCSharpExpression(Type type, ReadOnlyCollection<int> bounds, ReadOnlyCollection<Expression> expressions)
        {
            Type = type;
            Bounds = bounds;
            Expressions = expressions;
        }

        /// <summary>
        /// Returns the node type of this <see cref="CSharpExpression" />. (Inherited from <see cref="CSharpExpression" />.)
        /// </summary>
        /// <returns>The <see cref="CSharpExpressionType"/> that represents this expression.</returns>
        public sealed override CSharpExpressionType CSharpNodeType => CSharpExpressionType.NewMultidimensionalArrayInit;

        /// <summary>
        /// Gets the static type of the expression that this <see cref="Expression" /> represents. (Inherited from <see cref="Expression"/>.)
        /// </summary>
        /// <returns>The <see cref="Type"/> that represents the static type of the expression.</returns>
        public override Type Type { get; }

        /// <summary>
        /// Gets the bounds of the array.
        /// </summary>
        public ReadOnlyCollection<int> Bounds { get; }

        /// <summary>
        /// Gets the values to initialize the elements of the new array.
        /// </summary>
        public ReadOnlyCollection<Expression> Expressions { get; }

        /// <summary>
        /// Gets the <see cref="Expression" /> representing the element of the array the specified <paramref name="indexes"/>.
        /// </summary>
        /// <param name="indexes">The indexes of the element to retrieve.</param>
        /// <returns>An <see cref="Expression" /> representing the element of the array the specified <paramref name="indexes"/>.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", Justification = "Done by helper method.")]
        public Expression GetExpression(params int[] indexes)
        {
            ContractUtils.RequiresNotNull(indexes, nameof(indexes));

            if (indexes.Length != Bounds.Count)
            {
                throw Error.RankMismatch();
            }

            var index = 0;
            for (var i = 0; i < indexes.Length; i++)
            {
                var idx = indexes[i];
                var bound = Bounds[i];

                if (idx < 0 || idx >= bound)
                {
                    throw Error.IndexOutOfRange();
                }

                index = index * bound + idx;
            }

            return Expressions[index];
        }

        /// <summary>
        /// Dispatches to the specific visit method for this node type.
        /// </summary>
        /// <param name="visitor">The visitor to visit this node with.</param>
        /// <returns>The result of visiting this node.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", Justification = "Following the visitor pattern from System.Linq.Expressions.")]
        protected internal override Expression Accept(CSharpExpressionVisitor visitor)
        {
            return visitor.VisitNewMultidimensionalArrayInit(this);
        }

        /// <summary>
        /// Creates a new expression that is like this one, but using the supplied children. If all of the children are the same, it will return this expression.
        /// </summary>
        /// <param name="expressions">The <see cref="Expressions" /> property of the result.</param>
        /// <returns>This expression if no children changed, or an expression with the updated children.</returns>
        public NewMultidimensionalArrayInitCSharpExpression Update(IEnumerable<Expression> expressions)
        {
            if (expressions == Expressions)
            {
                return this;
            }

            return CSharpExpression.NewMultidimensionalArrayInit(Type.GetElementType(), Bounds, expressions);
        }

        /// <summary>
        /// Reduces the expression node to a simpler expression.
        /// </summary>
        /// <returns>The reduced expression.</returns>
        public override Expression Reduce()
        {
            // NB: Unlike the C# compiler, we don't optimize for the case where all elements are constants of
            //     a primitive type. In such a case, the C# compiler will emit a field with the binary representation
            //     of the whole array and emit a call to RuntimeHelpers.InitializeArray. To achieve this, we'd need
            //     to have access to a ModuleBuilder, also tying the reduction path to the compiler and requiring
            //     separate treatment for the interpreter.
            //
            //     This optimization would matter most if many copies of the array are initialized or the array is
            //     really big, and it only contains constants. In the case of a big array, we could argue that the
            //     current expression API has many ineffiencies already, e.g. when emitting closures including lots
            //     of captured variables, which does not create a display class but an array of StrongBox<T> objects.
            //
            //     An alternative, which could be useful if the expression is evaluated many times (creating many
            //     copies), would be to prepare an instance of the array, cache it, and use Array.Clone to create
            //     a copy each time the expression is evaluated. Effectively, the reduced expression would become a
            //     Constant node containing the fully materialized array. This would only be worth the effort if the
            //     cost of creating a clone is sufficiently less compared to element-by-element initialization which
            //     is likely true given it does a memberwise clone underneath. One drawback is that the expression
            //     would root the "prototype" of the array, but this is comparable to the module image containing a
            //     blob containing the array elements from which copies are created through InitializeArray (unless
            //     it employs a copy-on-write approach, haven't checked).
            //
            //     Note that the optimization sketched above could also be applied to ArrayInit nodes in LINQ, by
            //     changing the LambdaCompiler (see ILGen.EmitArray which generates a sequence of stelems). It
            //     doesn't seem like it was considered there, so maybe we can get away without it here as well.
            //
            //     Finally, not that the reduction approach below is likely more expensive than EmitArray used by
            //     the LambdaCompiler which can use dup instructions where we'll have a ldloc instruction for each
            //     element being initialized.

            var n = Expressions.Count;
            var rank = Bounds.Count;

            var res = Expression.Parameter(Type, "__array");
            var exprs = new Expression[n + 2];

            // NB: We need the bounds to NewArrayBounds and all values from 0 to each bound for ArrayAccess.
            var consts = Enumerable.Range(0, Bounds.Max() + 1).Select(CreateConstantInt32).ToArray();

            exprs[0] = Expression.Assign(res, Expression.NewArrayBounds(Type.GetElementType(), Bounds.Map(i => consts[i])));

            var indexValues = new int[rank];

            for (var i = 1; i <= n; i++)
            {
                var idx = i - 1;
                var value = Expressions[idx];

                for (var j = rank - 1; j >= 0; j--)
                {
                    var bound = Bounds[j];
                    indexValues[j] = idx % bound;
                    idx /= bound;
                }

                var indexes = new TrueReadOnlyCollection<Expression>(indexValues.Map(j => consts[j]));
                var element = Expression.ArrayAccess(res, indexes);

                exprs[i] = Expression.Assign(element, value);
            }

            exprs[n + 1] = res;

            return Expression.Block(new[] { res }, exprs);
        }
    }

    partial class CSharpExpression
    {
        /// <summary>
        /// Creates a <see cref="NewMultidimensionalArrayInitCSharpExpression"/> that represents creating a multi-dimensional array that has the specified bounds and elements. 
        /// </summary>
        /// <param name="type">A Type that represents the element type of the array.</param>
        /// <param name="bounds">The bounds of the array.</param>
        /// <param name="initializers">An array that contains Expression objects that represent the elements in the array.</param>
        /// <returns>An instance of the <see cref="NewMultidimensionalArrayInitCSharpExpression"/>.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", Justification = "Done by helper method.")]
        public static NewMultidimensionalArrayInitCSharpExpression NewMultidimensionalArrayInit(Type type, int[] bounds, params Expression[] initializers)
        {
            return NewMultidimensionalArrayInit(type, (IEnumerable<int>)bounds, initializers);
        }

        /// <summary>
        /// Creates a <see cref="NewMultidimensionalArrayInitCSharpExpression"/> that represents creating a multi-dimensional array that has the specified bounds and elements. 
        /// </summary>
        /// <param name="type">A Type that represents the element type of the array.</param>
        /// <param name="bounds">The bounds of the array.</param>
        /// <param name="initializers">An IEnumerable{T} that contains Expression objects that represent the elements in the array.</param>
        /// <returns>An instance of the <see cref="NewMultidimensionalArrayInitCSharpExpression"/>.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", Justification = "Done by helper method.")]
        public static NewMultidimensionalArrayInitCSharpExpression NewMultidimensionalArrayInit(Type type, int[] bounds, IEnumerable<Expression> initializers)
        {
            return NewMultidimensionalArrayInit(type, (IEnumerable<int>)bounds, initializers);
        }

        /// <summary>
        /// Creates a <see cref="NewMultidimensionalArrayInitCSharpExpression"/> that represents creating a multi-dimensional array that has the specified bounds and elements. 
        /// </summary>
        /// <param name="type">A Type that represents the element type of the array.</param>
        /// <param name="bounds">An IEnumerable{T} that contains the bounds of the array.</param>
        /// <param name="initializers">An IEnumerable{T} that contains Expression objects that represent the elements in the array.</param>
        /// <returns>An instance of the <see cref="NewMultidimensionalArrayInitCSharpExpression"/>.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", Justification = "Done by helper method.")]
        public static NewMultidimensionalArrayInitCSharpExpression NewMultidimensionalArrayInit(Type type, IEnumerable<int> bounds, IEnumerable<Expression> initializers)
        {
            ContractUtils.RequiresNotNull(type, nameof(type));
            ContractUtils.RequiresNotNull(bounds, nameof(bounds));
            ContractUtils.RequiresNotNull(initializers, nameof(initializers));

            if (type.Equals(typeof(void)))
            {
                throw LinqError.ArgumentCannotBeOfTypeVoid();
            }

            var boundsList = bounds.ToReadOnly();

            int dimensions = boundsList.Count;
            if (dimensions <= 0)
            {
                throw LinqError.BoundsCannotBeLessThanOne();
            }

            var length = 1;

            foreach (var bound in boundsList)
            {
                if (bound < 0)
                {
                    throw Error.BoundCannotBeLessThanZero();
                }

                checked
                {
                    length *= bound;
                }
            }

            var initializerList = initializers.ToReadOnly();

            if (initializerList.Count != length)
            {
                throw Error.ArrayBoundsElementCountMismatch();
            }

            var newList = default(Expression[]);
            for (int i = 0, n = initializerList.Count; i < n; i++)
            {
                var expr = initializerList[i];
                RequiresCanRead(expr, nameof(initializers));

                if (!TypeUtils.AreReferenceAssignable(type, expr.Type))
                {
                    if (!TryQuote(type, ref expr))
                    {
                        throw LinqError.ExpressionTypeCannotInitializeArrayType(expr.Type, type);
                    }

                    if (newList == null)
                    {
                        newList = new Expression[initializerList.Count];
                        for (int j = 0; j < i; j++)
                        {
                            newList[j] = initializerList[j];
                        }
                    }
                }

                if (newList != null)
                {
                    newList[i] = expr;
                }
            }

            if (newList != null)
            {
                initializerList = new TrueReadOnlyCollection<Expression>(newList);
            }

            return new NewMultidimensionalArrayInitCSharpExpression(type.MakeArrayType(boundsList.Count), boundsList, initializerList);
        }
    }

    partial class CSharpExpressionVisitor
    {
        /// <summary>
        /// Visits the children of the <see cref="NewMultidimensionalArrayInitCSharpExpression" />.
        /// </summary>
        /// <param name="node">The expression to visit.</param>
        /// <returns>The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:Validate arguments of public methods", Justification = "Following the visitor pattern from System.Linq.Expressions.")]
        protected internal virtual Expression VisitNewMultidimensionalArrayInit(NewMultidimensionalArrayInitCSharpExpression node)
        {
            return node.Update(Visit(node.Expressions));
        }
    }
}
