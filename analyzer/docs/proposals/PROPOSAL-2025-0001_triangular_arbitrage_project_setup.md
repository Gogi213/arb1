# Proposal: PROPOSAL-2025-0001 - Triangular Arbitrage Project Setup

## 1. Compact Diagnostic
The user requested the creation of a new project for finding triangular arbitrage opportunities across Binance, Bybit, and Gate.io. The new project should be structured analogously to the existing "Trader" project.

## 2. Proposed Change
Create a new top-level directory named `triangular_analyzer` to house the new project. Inside this directory, establish the following initial file and directory structure:

```
triangular_analyzer/
├── docs/
│   ├── proposals/
│   ├── issues/
│   ├── backlog.md
│   └── sprint1_initial_setup_plan.md
├── src/
│   └── __init__.py
├── .gitignore
├── README.md
└── requirements.txt
```

**File Content:**

- **`triangular_analyzer/docs/backlog.md`**:
  ```markdown
  # Project Backlog: Triangular Arbitrage Analyzer

  ## Epics
  - [ ] **EPIC-1**: Data Connectors for Exchanges
  - [ ] **EPIC-2**: Arbitrage Opportunity Identification Logic
  - [ ] **EPIC-3**: Order Execution and Management
  - [ ] **EPIC-4**: Monitoring and Reporting

  ## Tasks
  - [ ] **TASK-1**: Implement Binance data connector.
  - [ ] **TASK-2**: Implement Bybit data connector.
  - [ ] **TASK-3**: Implement Gate.io data connector.
  - [ ] **TASK-4**: Develop core logic for identifying triangular arbitrage paths.
  ```

- **`triangular_analyzer/docs/sprint1_initial_setup_plan.md`**:
  ```markdown
  # Sprint 1: Initial Setup and Binance Connector

  ## Goals
  - Establish the core project structure.
  - Implement the initial data connector for the Binance exchange.

  ## Tasks
  - [ ] Create project directory and initial documentation.
  - [ ] Implement the Binance WebSocket client to subscribe to order book data.
  - [ ] Define data structures for order books and trading pairs.
  ```

- **`triangular_analyzer/README.md`**:
  ```markdown
  # Triangular Arbitrage Analyzer

  A bot to identify and execute triangular arbitrage opportunities on Binance, Bybit, and Gate.io.
  ```

- **`triangular_analyzer/requirements.txt`**:
  ```
  # Initial dependencies will be added as connectors are developed.
  ```
  
- **`triangular_analyzer/.gitignore`**:
  ```
  # Python
  __pycache__/
  *.pyc
  *.pyo
  *.pyd
  .Python
  env/
  venv/
  
  # IDE
  .idea/
  .vscode/
  ```

## 3. Rationale
This change establishes a clean, isolated environment for the new project, preventing any interference with the existing `analyzer` project. The proposed structure adheres to the established project management methodology, creating a solid foundation for future development, including documentation, issue tracking, and source code management.

## 4. Risk Assessment
- **Risk:** None. This operation is additive and creates new files and directories without modifying any existing project components.
- **Mitigation:** N/A.

## 5. Testing Plan
- Verify that the `triangular_analyzer` directory and all its subdirectories and files are created successfully in the workspace root.
- Check that the content of the newly created files matches the content specified in this proposal.

## 6. Rollback Steps
- Execute the following command to remove the newly created project directory:
  `rm -rf triangular_analyzer`