// Sarthak Killedar
// 2026-11-10

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Onvif.Open.Core.StateMachines
{
    /// <summary>
    /// A state machine written down: its states, where it starts, and every allowed move. Code that changes a state
    /// asks it (<see cref="Move"/> refuses a move that is not in the table), tests check the code against it, and
    /// docs/STATE-MACHINES.md is generated from it (<see cref="ToMermaid"/>), so the three cannot drift apart.
    /// </summary>
    /// <typeparam name="TState">An enum or a string of known values.</typeparam>
    public sealed class StateMachine<TState> where TState : notnull
    {
        #region Constructor

        /// <param name="name">What it is about, e.g. "Export".</param>
        /// <param name="initial">The state a new one starts in.</param>
        /// <param name="transitions">Every allowed move, each with what makes it happen.</param>
        public StateMachine(string name, TState initial, params (TState From, TState To, string When)[] transitions)
        {
            Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A state machine needs a name.", nameof(name)) : name;
            Initial = initial;
            Transitions = transitions ?? throw new ArgumentNullException(nameof(transitions));

            var states = new List<TState> { initial };
            foreach (var (from, to, _) in transitions)
            {
                foreach (var state in new[] { from, to }.Where(s => !states.Contains(s)))
                {
                    states.Add(state);
                }
            }

            States = states;
            _next = States.ToDictionary(s => s, s => (IReadOnlyList<TState>)transitions.Where(t => Same(t.From, s)).Select(t => t.To).ToList());
        }

        #endregion

        #region Private Variables

        private readonly Dictionary<TState, IReadOnlyList<TState>> _next;

        #endregion

        #region Public Variables

        public string Name { get; }
        public TState Initial { get; }

        /// <summary>In the order they first appear: the initial state first.</summary>
        public IReadOnlyList<TState> States { get; }

        public IReadOnlyList<(TState From, TState To, string When)> Transitions { get; }

        /// <summary>States nothing leads out of.</summary>
        public IEnumerable<TState> Final => States.Where(s => Next(s).Count == 0);

        #endregion

        #region Public Methods

        /// <summary>Where it can go from <paramref name="state"/>.</summary>
        public IReadOnlyList<TState> Next(TState state) => _next.TryGetValue(state, out var next) ? next : Array.Empty<TState>();

        /// <summary>Whether it may go from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public bool CanMove(TState from, TState to) => Next(from).Any(s => Same(s, to));

        /// <summary><paramref name="to"/>, when the move is allowed; otherwise an <see cref="InvalidOperationException"/> naming the moves that are.</summary>
        public TState Move(TState from, TState to)
        {
            if (!CanMove(from, to))
            {
                var allowed = Next(from);
                throw new InvalidOperationException(allowed.Count == 0
                    ? $"{Name}: {from} is final; it cannot become {to}."
                    : $"{Name}: {from} cannot become {to}; it can become {string.Join(" or ", allowed)}.");
            }

            return to;
        }

        /// <summary>A Mermaid state diagram of the machine.</summary>
        public string ToMermaid()
        {
            var text = new StringBuilder().Append("stateDiagram-v2\n").Append("    [*] --> ").Append(Initial).Append('\n');
            foreach (var (from, to, when) in Transitions)
            {
                text.Append("    ").Append(from).Append(" --> ").Append(to);
                if (!string.IsNullOrWhiteSpace(when))
                {
                    text.Append(" : ").Append(when);
                }

                text.Append('\n');
            }

            foreach (var final in Final)
            {
                text.Append("    ").Append(final).Append(" --> [*]\n");
            }

            return text.ToString();
        }

        #endregion

        #region Private Methods

        private static bool Same(TState a, TState b) => EqualityComparer<TState>.Default.Equals(a, b);

        #endregion
    }
}
