#!/usr/bin/env python3
"""
CLI script to sanitize Excel files to clean CSV.

Usage:
    python scripts/sanitize_excel.py input.xlsx
    python scripts/sanitize_excel.py input.xlsx -o output.csv
    python scripts/sanitize_excel.py input.xlsx --keep-all-leagues
"""
import argparse
import sys
from pathlib import Path

# Add parent directory to path
sys.path.insert(0, str(Path(__file__).parent.parent))

from src.data.loaders.excel_sanitizer import ExcelSanitizer


def main():
    parser = argparse.ArgumentParser(
        description="Sanitize Excel/CSV files - keep only supported leagues and used columns"
    )
    parser.add_argument(
        "input",
        type=str,
        help="Path to input Excel or CSV file"
    )
    parser.add_argument(
        "-o", "--output",
        type=str,
        default=None,
        help="Path for output CSV file (optional, auto-generated if not provided)"
    )
    parser.add_argument(
        "--keep-all-leagues",
        action="store_true",
        help="Don't filter leagues, keep all"
    )
    parser.add_argument(
        "--summary",
        action="store_true",
        help="Print summary of the sanitized file"
    )
    
    args = parser.parse_args()
    
    input_path = Path(args.input)
    
    # Generate output path if not provided
    if args.output:
        output_path = Path(args.output)
    else:
        output_path = input_path.parent / f"{input_path.stem}_clean.csv"
    
    print(f"📂 Input:  {input_path}")
    print(f"📄 Output: {output_path}")
    print()
    
    # Sanitize
    sanitizer = ExcelSanitizer()
    df = sanitizer.sanitize(
        input_path,
        output_path,
        keep_all_leagues=args.keep_all_leagues,
    )
    
    if df is None or df.empty:
        print("❌ Sanitization failed!")
        sys.exit(1)
    
    print(f"\n✅ Success! Sanitized file saved to: {output_path}")
    
    # Print summary if requested
    if args.summary:
        summary = sanitizer.get_summary(df)
        print(f"\n📊 Summary:")
        print(f"   Total rows: {summary['total_rows']}")
        print(f"   Columns:    {', '.join(summary['columns'])}")
        print(f"   Leagues:    {summary['leagues']}")
        if summary['date_range']:
            print(f"   Date range: {summary['date_range']['min']} to {summary['date_range']['max']}")


if __name__ == "__main__":
    main()
