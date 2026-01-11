"""
Analyze Daily Ticket Qualification
Shows how many matches qualify for tickets per day with new filters
"""
import pandas as pd
from pathlib import Path
from datetime import datetime, timedelta
from collections import defaultdict
import sys

sys.path.insert(0, str(Path(__file__).parent.parent))

def analyze_daily_tickets(weeks=15):
    """Analyze how many matches qualify for tickets per day"""
    
    # Load backtest matches
    data_dir = Path(__file__).parent.parent / "data" / "historical"
    excel_file = list(data_dir.glob("*2025-2026.xlsx"))[0]
    
    # Read all leagues
    sheets = ['E0', 'E1', 'E2', 'E3', 'D1', 'D2', 'SP1', 'I1', 'I2', 'F1', 'F2']
    league_names = {
        'E0': 'Premier League', 'E1': 'Championship', 'E2': 'League One', 'E3': 'League Two',
        'D1': 'Bundesliga', 'D2': '2. Bundesliga',
        'I1': 'Serie A', 'I2': 'Serie B',
        'F1': 'Ligue 1', 'F2': 'Ligue 2',
        'SP1': 'La Liga'
    }
    
    # Filter by date
    end_date = datetime.now()
    start_date = end_date - timedelta(weeks=weeks)
    
    daily_stats = defaultdict(lambda: {
        'total': 0,
        'qualified': 0,
        'filtered_lower_league': 0,
        'filtered_min_odds': 0,
        'by_league': defaultdict(int)
    })
    
    LOWER_LEAGUES = ['Championship', 'League One', 'League Two']
    MIN_ODDS = 1.77
    
    for sheet in sheets:
        try:
            df = pd.read_excel(excel_file, sheet_name=sheet)
            df = df[(df['Date'] >= start_date) & (df['Date'] <= end_date)]
            league_name = league_names.get(sheet, sheet)
            
            for _, row in df.iterrows():
                date_str = row['Date'].strftime('%Y-%m-%d')
                daily_stats[date_str]['total'] += 1
                daily_stats[date_str]['by_league'][league_name] += 1
                
                # Get odds
                home_odds = row.get('B365H', row.get('PSH', 2.0)) or 2.0
                draw_odds = row.get('B365D', row.get('PSD', 3.3)) or 3.3
                away_odds = row.get('B365A', row.get('PSA', 3.5)) or 3.5
                
                # Determine favorite
                min_odds = min(home_odds, draw_odds, away_odds)
                
                # Apply lower league filter
                if league_name in LOWER_LEAGUES:
                    if 1.5 <= min_odds <= 2.5:
                        daily_stats[date_str]['filtered_lower_league'] += 1
                        continue
                
                # Check minimum odds
                if min_odds < MIN_ODDS:
                    daily_stats[date_str]['filtered_min_odds'] += 1
                    continue
                
                # Qualified!
                daily_stats[date_str]['qualified'] += 1
                
        except Exception as e:
            print(f"Error processing {sheet}: {e}")
    
    return daily_stats


def generate_daily_ticket_report(daily_stats):
    """Generate report showing daily ticket qualification"""
    
    report = []
    report.append("# Daily Ticket Qualification Analysis (15 Weeks)\n\n")
    report.append("**With Lower League Filters Applied**\n\n")
    report.append("---\n\n")
    
    # Convert to sorted list
    sorted_days = sorted(daily_stats.items())
    
    # Summary statistics
    total_matches = sum(day['total'] for day in daily_stats.values())
    total_qualified = sum(day['qualified'] for day in daily_stats.values())
    total_filtered_ll = sum(day['filtered_lower_league'] for day in daily_stats.values())
    total_filtered_odds = sum(day['filtered_min_odds'] for day in daily_stats.values())
    
    report.append("## Summary\n\n")
    report.append(f"- **Total Matches**: {total_matches}\n")
    report.append(f"- **Qualified for Tickets**: {total_qualified} ({total_qualified/total_matches*100:.1f}%)\n")
    report.append(f"- **Filtered (Lower League 1.5-2.5 odds)**: {total_filtered_ll} ({total_filtered_ll/total_matches*100:.1f}%)\n")
    report.append(f"- **Filtered (Min Odds <1.77)**: {total_filtered_odds} ({total_filtered_odds/total_matches*100:.1f}%)\n")
    report.append(f"- **Average Qualified per Day**: {total_qualified/len(sorted_days):.1f}\n\n")
    
    # Weekly breakdown
    report.append("## Weekly Breakdown\n\n")
    report.append("| Week | Days | Total Matches | Qualified | Avg/Day | Qualification Rate |\n")
    report.append("|------|------|---------------|-----------|---------|-------------------|\n")
    
    week_num = 1
    week_start = None
    week_matches = 0
    week_qualified = 0
    week_days = 0
    
    for i, (date_str, stats) in enumerate(sorted_days):
        date = datetime.strptime(date_str, '%Y-%m-%d')
        
        if week_start is None:
            week_start = date
        
        week_matches += stats['total']
        week_qualified += stats['qualified']
        week_days += 1
        
        # Check if week ended (7 days or last day)
        if week_days == 7 or i == len(sorted_days) - 1:
            avg_per_day = week_qualified / week_days if week_days > 0 else 0
            qual_rate = (week_qualified / week_matches * 100) if week_matches > 0 else 0
            report.append(f"| Week {week_num} | {week_days} | {week_matches} | {week_qualified} | {avg_per_day:.1f} | {qual_rate:.1f}% |\n")
            
            week_num += 1
            week_start = None
            week_matches = 0
            week_qualified = 0
            week_days = 0
    
    report.append("\n")
    
    # Daily details (sample - first 30 days)
    report.append("## Daily Details (First 30 Days)\n\n")
    report.append("| Date | Total | Qualified | Lower League Filtered | Min Odds Filtered |\n")
    report.append("|------|-------|-----------|----------------------|------------------|\n")
    
    for date_str, stats in sorted_days[:30]:
        report.append(f"| {date_str} | {stats['total']} | {stats['qualified']} | {stats['filtered_lower_league']} | {stats['filtered_min_odds']} |\n")
    
    report.append("\n")
    
    # Days with most qualifications
    report.append("## Top 10 Days (Most Qualified Matches)\n\n")
    top_days = sorted(daily_stats.items(), key=lambda x: x[1]['qualified'], reverse=True)[:10]
    
    report.append("| Date | Total Matches | Qualified | Leagues |\n")
    report.append("|------|---------------|-----------|----------|\n")
    
    for date_str, stats in top_days:
        leagues = ', '.join(f"{k}:{v}" for k, v in stats['by_league'].items() if v > 0)
        report.append(f"| {date_str} | {stats['total']} | {stats['qualified']} | {leagues} |\n")
    
    report.append("\n")
    
    # Days with least qualifications
    report.append("## Bottom 10 Days (Least Qualified Matches)\n\n")
    bottom_days = sorted(daily_stats.items(), key=lambda x: x[1]['qualified'])[:10]
    
    report.append("| Date | Total Matches | Qualified | Reason |\n")
    report.append("|------|---------------|-----------|--------|\n")
    
    for date_str, stats in bottom_days:
        if stats['filtered_lower_league'] > stats['filtered_min_odds']:
            reason = f"Lower league filter ({stats['filtered_lower_league']})"
        else:
            reason = f"Min odds ({stats['filtered_min_odds']})"
        report.append(f"| {date_str} | {stats['total']} | {stats['qualified']} | {reason} |\n")
    
    return ''.join(report)


if __name__ == "__main__":
    print("Analyzing daily ticket qualification for 15 weeks...")
    daily_stats = analyze_daily_tickets(weeks=15)
    
    report = generate_daily_ticket_report(daily_stats)
    
    # Save report
    output_path = Path.home() / ".gemini" / "antigravity" / "brain" / "3d63d768-8366-4689-9f50-05ba8a1be4a6" / "DAILY_TICKET_QUALIFICATION.md"
    with open(output_path, 'w') as f:
        f.write(report)
    
    print(f"✅ Analysis complete. Report saved to: {output_path}")
    print(f"\nTotal days analyzed: {len(daily_stats)}")
    total_matches = sum(day['total'] for day in daily_stats.values())
    total_qualified = sum(day['qualified'] for day in daily_stats.values())
    print(f"Average qualified per day: {total_qualified/len(daily_stats):.1f}")
    print(f"Qualification rate: {total_qualified/total_matches*100:.1f}%")
