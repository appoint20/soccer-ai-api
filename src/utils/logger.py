"""Logging configuration using loguru."""
import sys
from pathlib import Path
from loguru import logger


# Remove default handler
logger.remove()

# Flag to track if logging is set up
_logging_configured = False


def setup_logging(
    log_level: str = "DEBUG",
    log_file: str = "logs/app.log",
    console_level: str = "INFO",
    rotation: str = "10 MB",
    retention: int = 5,
) -> None:
    """
    Set up logging with console and file handlers.
    
    Args:
        log_level: Log level for file output
        log_file: Path to log file
        console_level: Log level for console output
        rotation: When to rotate log files (e.g., "10 MB", "1 day")
        retention: Number of backup files to keep
    """
    global _logging_configured
    
    if _logging_configured:
        return
    
    # Create logs directory if it doesn't exist
    log_path = Path(log_file)
    log_path.parent.mkdir(parents=True, exist_ok=True)
    
    # Console handler with colors
    logger.add(
        sys.stdout,
        format=(
            "<green>{time:YYYY-MM-DD HH:mm:ss}</green> | "
            "<level>{level: <8}</level> | "
            "<cyan>{name}</cyan>:<cyan>{function}</cyan>:<cyan>{line}</cyan> | "
            "<level>{message}</level>"
        ),
        level=console_level,
        colorize=True,
    )
    
    # File handler with rotation
    logger.add(
        log_file,
        format=(
            "{time:YYYY-MM-DD HH:mm:ss} | "
            "{level: <8} | "
            "{name}:{function}:{line} | "
            "{message}"
        ),
        level=log_level,
        rotation=rotation,
        retention=retention,
        compression="zip",
    )
    
    _logging_configured = True
    logger.info("Logging configured successfully")


def get_logger(name: str = "soccer-predictor"):
    """
    Get a logger instance with the given name.
    
    Args:
        name: Name for the logger (appears in log messages)
        
    Returns:
        Configured logger instance
    """
    # Ensure logging is set up
    if not _logging_configured:
        setup_logging()
    
    # Return logger bound to the name
    return logger.bind(name=name)


# Default setup with minimal config
def _default_setup():
    """Set up default logging configuration."""
    global _logging_configured
    if not _logging_configured:
        # Minimal console-only setup for imports
        logger.add(
            sys.stdout,
            format=(
                "<green>{time:YYYY-MM-DD HH:mm:ss}</green> | "
                "<level>{level: <8}</level> | "
                "<level>{message}</level>"
            ),
            level="INFO",
            colorize=True,
        )
        _logging_configured = True
