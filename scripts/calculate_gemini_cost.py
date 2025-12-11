"""
Gemini 3 Pro Cost Calculator
Based on pricing: ≤200k tokens: $2/$12 per million, >200k: $4/$18 per million
"""
import json
from pathlib import Path

REPORT_DIR = Path.home() / ".gemini" / "antigravity" / "brain" / "3d63d768-8366-4689-9f50-05ba8a1be4a6"

# Gemini 3 Pro Pricing (per million tokens)
PRICING = {
    'under_200k': {'input': 2.00, 'output': 12.00},
    'over_200k': {'input': 4.00, 'output': 18.00}
}

def estimate_tokens_for_match(match_data):
    """Estimate tokens for a single match analysis."""
    # Rough estimate based on JSON size
    json_str = json.dumps(match_data)
    
    # Input: prompt + match data
    # - System prompt: ~1500 tokens
    # - Match data: ~500 tokens per match
    # - Team stats: ~300 tokens per team = 600 tokens
    # Total input: ~2600 tokens per match
    
    input_tokens = 2600
    
    # Output: prediction response
    # - Analysis: ~200 tokens
    # - JSON response: ~100 tokens
    # Total output: ~300 tokens per match
    
    output_tokens = 300
    
    return input_tokens, output_tokens


def calculate_10_week_cost():
    """Calculate cost for 10-week backtest with 880 matches."""
    
    print("=" * 70)
    print("GEMINI 3 PRO COST ANALYSIS")
    print("=" * 70)
    
    # 10-week backtest data
    total_matches = 880
    leagues = 11
    
    print(f"\n📊 Backtest Scope:")
    print(f"   Total Matches: {total_matches}")
    print(f"   Leagues: {leagues}")
    print(f"   Avg per league: {total_matches/leagues:.0f} matches")
    
    # Calculate tokens
    input_per_match, output_per_match = estimate_tokens_for_match({})
    
    total_input_tokens = total_matches * input_per_match
    total_output_tokens = total_matches * output_per_match
    
    print(f"\n🔢 Token Estimates:")
    print(f"   Input per match: {input_per_match:,} tokens")
    print(f"   Output per match: {output_per_match:,} tokens")
    print(f"   Total input: {total_input_tokens:,} tokens ({total_input_tokens/1_000_000:.2f}M)")
    print(f"   Total output: {total_output_tokens:,} tokens ({total_output_tokens/1_000_000:.2f}M)")
    
    # Determine pricing tier
    total_tokens = total_input_tokens + total_output_tokens
    
    if total_tokens <= 200_000:
        pricing_tier = 'under_200k'
        tier_name = "≤200k tokens"
    else:
        pricing_tier = 'over_200k'
        tier_name = ">200k tokens"
    
    print(f"\n💰 Pricing Tier: {tier_name}")
    
    # Calculate costs
    input_cost = (total_input_tokens / 1_000_000) * PRICING[pricing_tier]['input']
    output_cost = (total_output_tokens / 1_000_000) * PRICING[pricing_tier]['output']
    total_cost = input_cost + output_cost
    
    print(f"\n💵 Cost Breakdown (Gemini 3 Pro):")
    print(f"   Input:  {total_input_tokens/1_000_000:.3f}M tokens × ${PRICING[pricing_tier]['input']:.2f} = ${input_cost:.4f}")
    print(f"   Output: {total_output_tokens/1_000_000:.3f}M tokens × ${PRICING[pricing_tier]['output']:.2f} = ${output_cost:.4f}")
    print(f"   {'=' * 50}")
    print(f"   TOTAL: ${total_cost:.4f}")
    
    # Compare to current (gemini-2.0-flash-exp - assumed free or cheap)
    print(f"\n📊 Comparison:")
    print(f"   Current Model: gemini-2.0-flash-exp")
    print(f"   Current Cost: ~$0.00 (experimental/free)")
    print(f"   Gemini 3 Pro: ${total_cost:.4f}")
    print(f"   Cost Increase: ${total_cost:.4f}")
    
    # Cost per match
    cost_per_match = total_cost / total_matches
    print(f"\n📈 Per-Match Cost:")
    print(f"   ${cost_per_match:.6f} per match")
    print(f"   ${cost_per_match * 100:.4f} per 100 matches")
    
    # Monthly/weekly estimates
    print(f"\n📅 Scaling Estimates:")
    weekly_cost = total_cost / 10  # 10 weeks
    monthly_cost = weekly_cost * 4
    
    print(f"   Per week: ${weekly_cost:.4f}")
    print(f"   Per month: ${monthly_cost:.4f}")
    print(f"   Per year: ${monthly_cost * 12:.2f}")
    
    # ROI context
    print(f"\n💡 ROI Context:")
    print(f"   Best strategy ROI: +301.9% (€30,796 profit)")
    print(f"   Gemini cost for 10 weeks: ${total_cost:.2f}")
    print(f"   Cost as % of profit: {total_cost/30796*100:.3f}%")
    
    print(f"\n✅ Gemini 3 Pro is AFFORDABLE for the value!")
    
    # Save report
    report_path = REPORT_DIR / "gemini_3_pro_cost_analysis.md"
    
    with open(report_path, 'w') as f:
        f.write("# Gemini 3 Pro Cost Analysis\\n\\n")
        f.write(f"## 10-Week Backtest Cost\\n\\n")
        f.write(f"- **Total Matches**: {total_matches}\\n")
        f.write(f"- **Input Tokens**: {total_input_tokens/1_000_000:.2f}M\\n")
        f.write(f"- **Output Tokens**: {total_output_tokens/1_000_000:.2f}M\\n")
        f.write(f"- **Pricing Tier**: {tier_name}\\n\\n")
        
        f.write(f"### Cost Breakdown\\n\\n")
        f.write(f"| Item | Tokens | Rate | Cost |\\n")
        f.write(f"|------|--------|------|------|\\n")
        f.write(f"| Input | {total_input_tokens/1_000_000:.3f}M | ${PRICING[pricing_tier]['input']:.2f}/M | ${input_cost:.4f} |\\n")
        f.write(f"| Output | {total_output_tokens/1_000_000:.3f}M | ${PRICING[pricing_tier]['output']:.2f}/M | ${output_cost:.4f} |\\n")
        f.write(f"| **Total** | | | **${total_cost:.4f}** |\\n\\n")
        
        f.write(f"### Scaling\\n\\n")
        f.write(f"- **Per week**: ${weekly_cost:.4f}\\n")
        f.write(f"- **Per month**: ${monthly_cost:.4f}\\n")
        f.write(f"- **Per year**: ${monthly_cost * 12:.2f}\\n\\n")
        
        f.write(f"### ROI Context\\n\\n")
        f.write(f"Best strategy (Strong Consensus) generated **€30,796 profit** in 10 weeks.\\n")
        f.write(f"Gemini 3 Pro cost: **${total_cost:.2f}** ({total_cost/30796*100:.3f}% of profit)\\n\\n")
        f.write(f"**Verdict**: ✅ Highly cost-effective!\\n")
    
    print(f"\\n📄 Report saved: {report_path}")
    print("=" * 70)


if __name__ == "__main__":
    calculate_10_week_cost()
