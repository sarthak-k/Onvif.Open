# Working on Onvif.Open

- Backend: .NET 10 in `Onvif.Open/` (`dotnet test Onvif.Open.sln`). Web app: React + TypeScript in `web/`
  (`npm run typecheck`, `npm test`, `npm run e2e`).
- **State machines.** Anything with a lifecycle (a status, a step, a phase) is declared once as a table:
  `StateMachine<T>` (`Onvif.Open.Core.StateMachines`) or `defineMachine` (`web/src/shared/stateMachine.ts`). Change a
  state only through its table (`Move` / `move`); never assign a status string directly. The list of machines, where
  each lives and what changes it is in `docs/STATE-MACHINES.md`. After editing a table, run the tests with
  `UPDATE_STATE_DOCS=1` to redraw its diagram; a new machine also goes in the list in `StateMachinesTest` or
  `web/src/shared/stateMachines.test.ts` and gets a section in the doc.
- State names are the API's strings and enums: adding one is compatible, renaming one is not.
