#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Unified logging for console and file output.
"""

import logging
import os
from datetime import datetime

def setup_logger():
    """
    Sets up a logger that outputs to both console and a file.
    
    Returns:
        A configured logger instance.
    """
    # Create logs directory if it doesn't exist
    logs_dir = 'logs'
    if not os.path.exists(logs_dir):
        os.makedirs(logs_dir)

    # Generate a unique log file name with a timestamp
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    log_filename = os.path.join(logs_dir, f"backtest_{timestamp}.log")

    # Create a logger
    logger = logging.getLogger('ArbitrageBacktester')
    logger.setLevel(logging.DEBUG)

    # Create a formatter
    formatter = logging.Formatter('%(asctime)s - %(levelname)s - %(message)s', datefmt='%Y-%m-%d %H:%M:%S')

    # Create a file handler
    file_handler = logging.FileHandler(log_filename, encoding='utf-8')
    file_handler.setLevel(logging.DEBUG)
    file_handler.setFormatter(formatter)

    # Create a console handler
    console_handler = logging.StreamHandler()
    console_handler.setLevel(logging.INFO) # Keep console clean
    console_handler.setFormatter(formatter)

    # Add handlers to the logger, but only if they haven't been added before
    if not logger.handlers:
        logger.addHandler(file_handler)
        logger.addHandler(console_handler)

    return logger

# Create a default logger instance to be imported by other modules
log = setup_logger()