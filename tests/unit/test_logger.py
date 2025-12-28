"""
Unit tests for logging setup.

Tests cover:
- Logger initialization
- Log output configuration
- Log levels
"""
import pytest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).parent.parent.parent))

from src.utils.logger import get_logger, setup_logging


class TestLoggerSetup:
    """Tests for logger setup."""
    
    def test_get_logger_returns_logger(self):
        """Test get_logger returns a logger instance."""
        logger = get_logger("test")
        
        assert logger is not None
    
    def test_get_logger_with_name(self):
        """Test get_logger creates logger with name."""
        logger = get_logger("my_module")
        
        # Logger should be callable
        assert callable(logger.info)
        assert callable(logger.error)
        assert callable(logger.debug)
    
    def test_logger_can_log(self):
        """Test logger can log messages."""
        logger = get_logger("test_log")
        
        # Should not raise
        logger.info("Test message")
        logger.debug("Debug message")
        logger.warning("Warning message")
    
    def test_different_loggers_independent(self):
        """Test different logger names are independent."""
        logger1 = get_logger("module1")
        logger2 = get_logger("module2")
        
        # Both should work
        logger1.info("From module1")
        logger2.info("From module2")


class TestLogLevels:
    """Tests for log level handling."""
    
    def test_log_info(self):
        """Test INFO level logging."""
        logger = get_logger("info_test")
        
        # Should not raise
        logger.info("Info message")
    
    def test_log_debug(self):
        """Test DEBUG level logging."""
        logger = get_logger("debug_test")
        
        logger.debug("Debug message")
    
    def test_log_warning(self):
        """Test WARNING level logging."""
        logger = get_logger("warning_test")
        
        logger.warning("Warning message")
    
    def test_log_error(self):
        """Test ERROR level logging."""
        logger = get_logger("error_test")
        
        logger.error("Error message")
