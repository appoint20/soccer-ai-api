"""Setup configuration for soccer-predictor package."""
from setuptools import setup, find_packages

setup(
    name="soccer-predictor",
    version="0.1.0",
    description="European Soccer Match Prediction API",
    author="Your Name",
    author_email="your.email@example.com",
    packages=find_packages(where="src"),
    package_dir={"": "src"},
    python_requires=">=3.10",
    install_requires=[
        "pandas>=2.1.0",
        "numpy>=1.26.0",
        "openpyxl>=3.1.0",
        "python-dateutil>=2.8.0",
        "pyyaml>=6.0.0",
        "python-dotenv>=1.0.0",
        "loguru>=0.7.0",
    ],
    extras_require={
        "dev": [
            "pytest>=7.4.0",
            "pytest-cov>=4.1.0",
            "black>=23.11.0",
            "flake8>=6.1.0",
            "mypy>=1.7.0",
        ],
    },
    entry_points={
        "console_scripts": [
            "soccer-predictor=main:main",
        ],
    },
)
