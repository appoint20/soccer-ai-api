# SoccerAI monetization and audience research

Research date: **9 September 2026**. All external sources below were accessed on that date. Germany is the initial working assumption because the app supports German and this workspace is in Berlin; the owner has not yet confirmed the launch market. Austria, Switzerland, the UK and the US require separate pricing and market assumptions. This document proposes experiments; it does not change App Store prices, activate ads, or claim measured demand for SoccerAI.

## Recommendation

Build a subscription business around **reliable football research that saves time**: fresh odds, clear probabilities, useful league/market filters, and an honest record of every published prediction. Begin with a free tier and one paid tier. Test **€7.99 versus €9.99 per month**. Consider **€79.99 per year** alongside the €9.99 monthly option after users demonstrate recurring value. Keep ads secondary; defer bookmaker affiliate deals and a higher priced professional tier.

Some bettors will pay for useful research. We do not yet know how many of *our* users will pay, or their price ceiling. Weekly betting is a sign of repeated need, not evidence of willingness to buy another subscription. A profitable app is possible without promising that its users will profit from bets. An unsupported “80% accurate” message would undermine the trust this business needs.

## What the evidence actually says

| Evidence | Observation | What it can establish |
| --- | --- | --- |
| Germany, 2025 population survey | Among 12,340 respondents aged 16–70, sports betting participation in the previous year was 3.9%; online sports betting was 3.0%. Sports participation rates were 6.4% for men and 1.3% for women. | A real, male-skewed category; **not** the number of weekly adult football bettors or subscription buyers. [ISD/University of Bremen survey, table 5, p.27](https://www.isd-hamburg.de/wp-content/uploads/2026/03/Glueckspielsurvey_2025.pdf) |
| US AI-assistance survey, April 2023 | 70% of 286 monthly bettors expressed willingness to pay for hypothetical AI assistance. The same report records substantial expectation of a free service and interest in trying it first. | Directional interest in the concept. Old, small, US, hypothetical, no specific price; **not** 70% conversion or evidence for Germany. [YouGov report, p.16](https://commercial.yougov.com/rs/464-VHH-988/images/WP-2023-10-Global-Gambling-Report.pdf) |
| US betting subscription survey, February 2025 | YouGov's article reports 25% willing to pay less than $10/month, 17% $10–19, and 12% $20 or more. | Price sensitivity exists. The product included bookmaker parlay boosts and other benefits, unlike our app. No transfer of these percentages or dollar prices to German football analytics. [YouGov primary article](https://yougov.com/en-us/articles/51689-exploring-the-appeal-of-betting-subscriptions-that-offer-parlay-boosts-among-us-bettors) |
| Actual mobile subscription transactions, 2026 report | RevenueCat reports median freemium download-to-paid conversion by day 35 of 2.1%, versus 10.7% for hard-paywall apps. Its year-one cohort retention comparison shows 28% for annual, 8% monthly and 1.2% weekly plans. | A reality check against survey enthusiasm. The dataset uses RevenueCat customers with qualifying revenue/installs, not all apps, and is not specific to betting. This is observational, not proof that changing a paywall causes those results. **Download conversion is not the percentage of monthly active users paying.** [RevenueCat 2026 report and methodology](https://www.revenuecat.com/state-of-subscription-apps/) |

Evidence limitation: no representative, current German study directly measuring willingness to pay for an independent football prediction app was found in this research. We have no SoccerAI installs, retention, verified subscriptions, acquisition costs, or customer interviews to replace that gap. Do not present a market-size or conversion forecast as measured fact.

Source quality note: a secondary article about the 2025 YouGov survey gives materially different price percentages. This document uses the primary YouGov page and does not combine the two. The 2023 PDF's actual survey date is April 2023; a recent crawl/upload date does not make it a 2026 survey.

## Competitor prices we can verify

These are public offers observed today, not evidence of subscriber counts, realized revenue, conversion or profitable predictions. Discounts, currencies and platform prices can differ.

| Product | Observed offer | Relevant comparison |
| --- | --- | --- |
| BetMines, German iOS App Store | **€21.99 / 1 month**, €54.99 / 3 months, **€184.99 / 12 months** | Football tips, statistics and filters with a free entry tier. [App Store listing](https://apps.apple.com/de/app/betmines-fu%C3%9Fball-wetten-tipps/id1477625153) |
| FootyStats, German iOS App Store | **€26.99** Premium in-app purchase; the public listing does not disclose its billing interval | Statistics and research competitor. Do not label that IAP “monthly” without checking the purchase screen. [App Store listing](https://apps.apple.com/de/app/footystats-fu%C3%9Fball-statistiken/id1590091942) |
| OddAlerts, official website | **£19.99 / month** Pro; **£69.99 / month** advanced/API offer | Broader odds alerts, filtering and research tools. GBP is kept as quoted; no exchange-rate conversion is implied. [Official pricing](https://www.oddalerts.com/pro) |
| Betaminic Builder, official website | **€29.90 / 7 days**, 100 picks, manual renewal | Specialist strategy product, not directly comparable to a simple mobile subscription. [Official pricing](https://www.betaminic.com/betamin-builder/pricing/) |

Charging below these offers is a sensible experiment for a new product with less established evidence. It does not itself create demand; free alternatives still compete for the same user.

## Who to build for, and what to test

The price ranges below are **product hypotheses**, not survey estimates.

| Segment | Repeated need | Likely offer to test |
| --- | --- | --- |
| Casual football follower or occasional bettor | Quick context, favorite club, simple match facts | Free; may never need paid access |
| Weekly recreational bettor who researches matches | Compare a short weekend list without checking many sites; spot outdated odds | **€7.99–9.99/month** for convenience, saved filters and alerts |
| Consistent research hobbyist | Detailed market/league records, probability history, custom filters and exports | Later **€14.99–19.99/month**, if those tools exist and get repeat use |
| Analyst, publisher or small media business | Embeddable verified records, exports or licensed data delivery | Separate paid pilot; do not infer a B2B price or resell provider data before checking the license |

Prioritize behavior over stereotypes: adult users who already spend time researching the leagues we cover well. Do not target people because they are losing, increasing stakes, or showing signs of gambling harm. The German survey includes minors for research; that is not a target-customer definition.

A useful first paid experience is: save a market/league filter, review a match with current odds and evidence, return the next weekend, and understand what changed. The product should still feel worthwhile after several losing predictions. More combinations, more notifications, and a more confident AI voice are poor substitutes for that.

## Packaging

**Free:** favorite leagues, a usable sample of current match analysis, visible odds timestamp, explanation of probability, and public aggregate results that include losses. Never hide the evidence needed to judge the product's claims behind a subscription.

**Premium:** the complete analysis/filtering workflow, saved lists, configurable alerts when an observed price crosses a user threshold, deeper league/market breakdowns, odds/probability history, and a personal research journal. Alerts must reflect the actual refresh cadence: a three-hour feed cannot honestly be marketed as instant odds monitoring. Cache common fixture research so paid value does not require an expensive model call for every screen view.

Use one subscription group with monthly and annual options. Show the total annual charge and renewal terms clearly. The proposed €79.99 annual price is €6.67/month equivalent, about 33% below twelve €9.99 payments; it also reduces our monthly revenue per subscriber. Avoid lifetime access while the product incurs ongoing data costs. A non-renewing weekend pass around €2.99 is a later experiment for subscription-averse users, not the initial default.

Do not change live pricing automatically based on this report. The repository's StoreKit test configuration currently contains 6.99 monthly and 59.99 yearly values, while localization has a €7.99 fallback. These are not verified App Store Connect live prices. The purchase interface should always use the loaded StoreKit product's localized price.

## Ads, subscriptions and other revenue

1. **Subscriptions first:** predictable payment for continued research utility and a direct incentive to retain trust. Keep complete performance evidence public.
2. **Contextual ads or sponsorship second:** consider clearly labeled football-related placements in free screens once there is sufficient traffic. Run a holdout to measure whether revenue compensates for lost retention and subscriptions. No ad should obscure a fresh-odds warning or pretend to be an independent pick.
3. **Bookmaker affiliates later, if at all:** these can conflict with impartial recommendations. In Germany, §5(6) specifically restricts variable compensation for applicable online gambling advertising, including affiliate links; §5(7) prohibits advertising for unlicensed gambling. Do not budget revenue share, deposit-based, or stake-based commissions as an available business model. A licensed operator on the whitelist is not blanket approval for every placement or contract. [Current GlüStV text](https://bravors.brandenburg.de/vertraege/gluestv_2021#5)
4. **B2B later:** evidence widgets and research exports could diversify revenue after proof and data-rights checks. They require a separate buyer and sales process, not a consumer paywall extension.

Apple's standard Small Business Program commission is 15% for eligible enrolled developers; eligibility includes the $1m proceeds threshold and associated accounts. Without that program, standard subscriptions generally pay the developer 70% in the subscriber's first year and 85% after one paid year, less applicable taxes. EU alternative terms differ; the estimates below assume standard terms. [Apple Small Business Program](https://developer.apple.com/app-store/small-business-program/), [Apple subscriptions](https://developer.apple.com/app-store/subscriptions/)

Selling analysis differs from taking wagers. Adding real-money betting would introduce Apple's licensing, geographical restriction and free-download requirements under 5.3.4. Digital premium functionality must also follow the applicable purchase rules. Assess the actual product and jurisdictions before adding bet placement. [App Review Guidelines](https://developer.apple.com/app-store/review/guidelines/)

## Illustrative economics — assumptions, not a forecast

Assume €9.99 monthly consumer price, **19% VAT for this example**, 15% Apple commission, no refunds, and €0.50 incremental monthly cost per paying user. Actual tax, refunds, costs and proceeds must come from store reports and invoices. This example excludes annual subscribers.

`Proceeds per subscriber = €9.99 / 1.19 × 0.85 = €7.14`

`Contribution before fixed costs and acquisition = €7.14 − €0.50 = €6.64`

| Active monthly subscribers | Gross monthly consumer billings | Approximate contribution before fixed costs/acquisition |
| ---: | ---: | ---: |
| 100 | €999 | €664 |
| 500 | €4,995 | €3,318 |
| 1,000 | €9,990 | €6,636 |
| 1,507 | €15,055 | €10,000 |

This is not operating profit: subtract fixed API/hosting costs, serving free users, customer support, engineering, refunds, tax obligations and acquisition costs. At €79.99/year, equivalent monthly store proceeds in the same VAT/commission example fall to **€4.76 before costs**. Annual cash received upfront is not monthly recurring cash received twelve times.

For ads, take a deliberately hypothetical **24 actual ad impressions per free active user per month** and **€2–6 net eCPM**. That produces about **€0.05–0.14 per such user/month**, or **€480–1,440 at 10,000 free active users**. These eCPMs are sensitivity inputs, not an industry promise. Use actual displayed impressions, not ad requests. Google's definition is earnings per thousand impressions. [AdMob eCPM](https://support.google.com/admob/answer/15337570?hl=en)

Google's gambling-content restriction has country exclusions including Germany; it is not correct to claim all gambling-adjacent apps are universally barred from ads. Eligibility depends on content, users' locations and all applicable policies. This adds another reason to measure real ad serving before building a budget around it. [Google Publisher Restrictions](https://support.google.com/publisherpolicies/answer/10437963?hl=en-GB)

Do not fund paid acquisition from a guessed lifetime value. Under the monthly example, a customer retained for three paid months contributes only about €19.91 before fixed costs. An acquisition cost above that loses money within that period. Measure cohort payback and refunds first.

## Validation plan

**Before asking users to pay:** make current odds, recorded prediction statistics and basic reliability demonstrably work. Complete subscription entitlement verification, renewal/expiry/refund handling, restore purchases and server access control for premium-only resources. The root implementation audit found that backend Apple verification was absent and premium analysis could be delivered to free clients; hiding a view does not protect a paid API. Recheck the final implementation before launch. Do not treat local entitlement flags, Base64 JSON or simulator transactions as verified revenue.

**Weeks 1–2:** recruit 20–30 consenting adults who already bet on football weekly; include current paying tool users and people using only free tools. No outreach was sent as part of this research. Ask them to demonstrate their last research session, what they actually paid for, what took time, what disappointed them and which free alternative they would use. Show a working flow and its real record. Ask about a specific proposed offer only after that demonstration. Interviews discover needs; they do not estimate population percentages.

**Weeks 3–8:** run a real, clearly disclosed monthly purchase test at €7.99 versus €9.99 with identical features, eligibility, trial terms and acquisition sources. Randomize eligible new users, keep assignments consistent and avoid changing prices for existing subscribers. If traffic is too small for a defensible comparison, use one transparent launch price and report the small sample honestly. Do not declare a winner because five buyers chose one variant. A two-week preview can expose two weekends of product use; no number of trial matches establishes a reliable predictive win rate.

Measure install → activation → paywall → trial → **verified payment**, first renewal, cancellation, refund, support contact, paid feature use, and net contribution per eligible user at 60–90 days. Define activation as completing useful research and saving a filter/watchlist, rather than placing more bets. Deduplicate restored transactions. Separate downloads, monthly active users, trial users and paying subscribers in reports.

Predefine the minimum effect and sample size after observing baseline conversion. Choose based on net contribution with retention/refund guardrails, not clicks on “subscribe.” Survey answers, paywall taps and test transactions must remain separate from purchases. Add the annual plan after recurring use is visible; assess annual renewal over an actual year.

For retention, use opt-in summaries, saved research, transparent model changes and honest post-match explanations. Let users manage/cancel through the standard subscription UI. Avoid loss-chasing messages, artificial countdowns, rewards for staking or misleading “guaranteed winner” language.

The next business inputs needed are launch countries, existing active users and returning-weekend users, actual subscription transactions/refunds, current API/LLM/hosting bills, and available distribution channels. Those determine whether €7.99, €9.99 or a different product is viable.
