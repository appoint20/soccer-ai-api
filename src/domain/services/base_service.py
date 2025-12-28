"""Base service class for domain services."""
from abc import ABC, abstractmethod
from typing import Generic, TypeVar

from src.utils.logger import get_logger

T = TypeVar("T")


class BaseService(ABC, Generic[T]):
    """
    Abstract base class for domain services.
    
    Provides common functionality like logging and error handling
    for all domain services.
    """
    
    def __init__(self, service_name: str):
        """
        Initialize the base service.
        
        Args:
            service_name: Name of the service for logging
        """
        self.logger = get_logger(service_name)
    
    @abstractmethod
    def execute(self, *args, **kwargs) -> T:
        """
        Execute the main service operation.
        
        This method should be implemented by all concrete services.
        
        Returns:
            Service result of type T
        """
        pass
    
    def log_info(self, message: str) -> None:
        """Log an info message."""
        self.logger.info(message)
    
    def log_error(self, message: str, error: Exception = None) -> None:
        """Log an error message with optional exception."""
        if error:
            self.logger.error(f"{message}: {str(error)}")
        else:
            self.logger.error(message)
    
    def log_warning(self, message: str) -> None:
        """Log a warning message."""
        self.logger.warning(message)
    
    def log_debug(self, message: str) -> None:
        """Log a debug message."""
        self.logger.debug(message)
