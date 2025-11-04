graph TD
    A[Start: run_all_ultra.py] --> B{Parse CLI Arguments};
    B --> C[discover_data];
    C --> D{Symbols found?};
    D -- Yes --> E[Create Symbol Batches];
    D -- No --> F[End];
    E --> G[Start Multiprocessing Pool];
    
    subgraph Worker Process
        H[analyze_symbol_batch] --> I[Load Exchange Data in Parallel];
        I --> J[analyze_pair_fast];
        J --> K[Synchronize Data (join_asof)];
        K --> L[Calculate Metrics];
        L --> M[Return Stats];
    end

    G -- For each symbol batch --> H;
    M --> N[Collect Results];
    G -- All batches processed --> N;
    
    N --> O{Any successful results?};
    O -- Yes --> P[Save to CSV and Print Top 10];
    O -- No --> Q[Print Summary];
    P --> Q;
    Q --> R[End];
