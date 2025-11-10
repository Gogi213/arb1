# Project Backlog

This document tracks all identified tasks, feature requests, and technical debt.

## Feature Requests

- **[FB-001] Asymmetric Opportunity Analysis**:
  - **Description**: Enhance the analyzer to calculate opportunities with directional bias. For a pair (Ex1, Ex2), an opportunity is profitable only if `price_Ex2 > price_Ex1 * (1 + fee)`. The reverse trade (`Ex2 -> Ex1`) has a different condition. The tool should analyze these one-way arbitrage opportunities, accounting for trading fees.
  - **Status**: To Do

## Technical Debt

- **[TD-001] Hardcoded Thresholds**:
  - **Description**: The deviation thresholds `[0.3, 0.5, 0.4]` are hardcoded in `run_all_ultra.py`. They should be configurable, possibly via command-line arguments.
  - **Status**: To Do

## Documentation

- **[DOC-001] Update Role Definition**:
  - **Description**: The `role_definition.md` may need updates to reflect the latest project state and workflow.
  - **Status**: Done
