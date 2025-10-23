#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Unified logging for console and file output.
"""

import logging
import os
from datetime import datetime
from typing import Dict

class LoggerManager:
    """
    Manages the setup of loggers for a single backtest session.
    Ensures that each session has its own isolated log directory.
    """
    def __init__(self, session_name: str = "backtest"):
        """
        Initializes the logger manager for a new session.
        
        Args:
            session_name: A prefix for the log directory name.
        """
        self.loggers: Dict[str, logging.Logger] = {}
        self.logs_dir: str = ""
        self._setup_loggers(session_name)

    def _setup_loggers(self, session_name: str):
        """
        Sets up loggers for different categories: system, indicator, final, and trade.
        """
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        self.logs_dir = os.path.join('logs', f"{session_name}_{timestamp}")
        if not os.path.exists(self.logs_dir):
            os.makedirs(self.logs_dir)

        log_configs = {
            'system': 'system.log',
            'indicator': 'indicators.log',
            'summary': 'summary.log',
            'trade': 'trades.log'
        }

        for name, filename in log_configs.items():
            logger = logging.getLogger(f"{timestamp}_{name}")
            logger.setLevel(logging.DEBUG)
            logger.propagate = False # Prevent passing logs to the root logger

            # Prevent duplicate handlers
            if logger.hasHandlers():
                logger.handlers.clear()

            log_filename = os.path.join(self.logs_dir, filename)
            
            formatter = logging.Formatter('%(asctime)s - %(levelname)s - %(message)s', datefmt='%Y-%m-%d %H:%M:%S')

            # File handler
            file_handler = logging.FileHandler(log_filename, encoding='utf-8')
            file_handler.setLevel(logging.INFO)
            file_handler.setFormatter(formatter)
            logger.addHandler(file_handler)

            # Console handler for system logs
            if name == 'system':
                console_handler = logging.StreamHandler()
                console_handler.setLevel(logging.INFO)
                console_handler.setFormatter(formatter)
                logger.addHandler(console_handler)
            
            self.loggers[name] = logger

    def get_logger(self, name: str) -> logging.Logger:
        """
        Retrieves a configured logger by name.
        
        Args:
            name: The name of the logger (e.g., 'system', 'indicator').
            
        Returns:
            A configured logger instance.
        """
        return self.loggers.get(name, logging.getLogger()) # Return default logger if not found
