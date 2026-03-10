using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MineralKingdom.Infrastructure.Migrations
{
    public partial class S11_1_SeedPublishedCmsPublicPages : Migration
    {
        private static readonly Guid CmsSeedUserId = new("00000000-0000-0000-0000-000000000111");

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BuildInsertSql(
                slug: "about",
                markdown: AboutMarkdown(),
                title: "About"));

            migrationBuilder.Sql(BuildInsertSql(
                slug: "faq",
                markdown: FaqMarkdown(),
                title: "FAQ"));

            migrationBuilder.Sql(BuildInsertSql(
                slug: "privacy",
                markdown: PrivacyMarkdown(),
                title: "Privacy Policy"));

            migrationBuilder.Sql(BuildInsertSql(
                slug: "terms",
                markdown: TermsMarkdown(),
                title: "Terms & Conditions"));

            migrationBuilder.Sql(BuildInsertSql(
                slug: "auction-rules",
                markdown: AuctionRulesMarkdown(),
                title: "Auction Rules"));

            migrationBuilder.Sql(BuildInsertSql(
                slug: "buying-rules",
                markdown: BuyingRulesMarkdown(),
                title: "Buying Rules"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                delete from cms_page_revisions
                where "ChangeSummary" = 'Initial seeded public page content'
                  and "PageId" in (
                    select "Id"
                    from cms_pages
                    where "Slug" in ('about', 'faq', 'privacy', 'terms', 'auction-rules', 'buying-rules')
                  );
                """);
        }

        private static string BuildInsertSql(string slug, string markdown, string title)
        {
            var escapedMarkdown = SqlLiteral(markdown);

            return $"""
                insert into cms_page_revisions
                ("Id", "PageId", "Status", "ContentMarkdown", "ContentHtml", "EditorUserId", "PublishedByUserId", "ChangeSummary", "CreatedAt", "PublishedAt", "EffectiveAt")
                select
                  gen_random_uuid(),
                  p."Id",
                  'PUBLISHED',
                  '{escapedMarkdown}',
                  null,
                  '{CmsSeedUserId}',
                  '{CmsSeedUserId}',
                  'Initial seeded public page content',
                  now(),
                  now(),
                  now()
                from cms_pages p
                where p."Slug" = '{SqlLiteral(slug)}'
                  and not exists (
                    select 1
                    from cms_page_revisions r
                    where r."PageId" = p."Id"
                      and r."Status" = 'PUBLISHED'
                  );
                """;
        }

        private static string SqlLiteral(string value) => (value ?? "").Replace("'", "''");

        private static string AboutMarkdown() => """
# About Mineral Kingdom

Mineral Kingdom is a curated online destination for minerals, crystals, and natural treasures. We created Mineral Kingdom to make it easier for collectors, enthusiasts, and curious newcomers to discover unique pieces with confidence.

Our goal is simple: offer beautiful, interesting specimens through a trustworthy buying experience. Whether you are browsing a fixed-price listing, participating in an auction, or learning the basics of collecting, we want Mineral Kingdom to feel welcoming, transparent, and easy to use.

## What We Offer

Mineral Kingdom features a growing selection of:
- mineral specimens
- crystals
- collector pieces
- special auction items
- fixed-price listings for direct purchase

Some items are listed with a set purchase price, while others are offered through timed auctions. This gives members multiple ways to shop based on their interests, budget, and collecting style.

## Our Approach

We believe the best collecting experience combines excitement with clarity. That means we aim to provide:
- accurate listings and photographs
- clear auction and buying rules
- straightforward communication
- secure checkout and account tools
- a fair and respectful marketplace environment

We want customers to understand what they are buying, how the transaction works, and what to expect after purchase.

## Built for Collectors and Curious Buyers

Mineral Kingdom is designed for both experienced collectors and people just getting started. Some visitors arrive knowing exactly what they are looking for. Others are still learning what they like. Both are welcome here.

Our platform is meant to support discovery, collecting, and ongoing engagement through:
- member accounts
- auction participation
- direct purchases
- saved information for smoother checkout and communication

## Trust Matters

We know that online buying depends on trust. That is why our policies, rules, and account systems are intended to support a secure and reliable experience. We encourage all members to review our Privacy Policy, Terms & Conditions, Auction Rules, and Buying Rules before participating.

## Join the Kingdom

Mineral Kingdom is more than a storefront. It is a place for people who appreciate natural beauty, collecting, and the thrill of finding something special.

Thank you for visiting Mineral Kingdom and being part of the journey.
""";

        private static string FaqMarkdown() => """
# Frequently Asked Questions

## What is Mineral Kingdom?

Mineral Kingdom is an online platform where members can browse, purchase, and bid on minerals, crystals, and related collector items.

## Do I need an account to buy or bid?

You may be able to browse public listings without an account, but an account is required for member features such as bidding, managing purchases, and accessing account-related tools.

## How do auctions work?

Auction items are available for bidding during a set time period. The highest valid bid at the close of the auction wins, subject to any applicable auction rules. Please review the Auction Rules page for complete details.

## How do fixed-price purchases work?

Fixed-price items can be purchased directly at the listed price, subject to availability. Once checkout is completed successfully, the order will move into processing according to our normal order flow.

## How do I know whether an item is an auction or a direct purchase?

Each listing will indicate whether it is being sold as:
- an auction item, or
- a fixed-price item available for immediate purchase

Be sure to review the listing details carefully before participating.

## Are all sales final?

Specific terms may vary depending on the item and transaction type. Please review our Terms & Conditions, Buying Rules, and any listing-specific details before making a purchase or placing a bid.

## What payment methods are accepted?

Accepted payment methods are presented during checkout and may include third-party payment providers supported by Mineral Kingdom. Payment availability may change over time.

## Is my payment information stored by Mineral Kingdom?

Mineral Kingdom does not store your debit card, credit card, or bank payment details directly. Payment handling is performed through supported third-party payment providers in accordance with our Privacy Policy.

## Can I cancel a bid?

In general, bids should be placed carefully and intentionally. Auction participation is governed by the Auction Rules, which explain how bidding works and what obligations apply when a bid is placed.

## Can I update my account information?

Yes. You are responsible for keeping your account information accurate and up to date, including your email address, shipping information, and other relevant details.

## How do I delete my account or request changes to my data?

Please refer to our Privacy Policy for information about accessing, correcting, or requesting deletion of personal data.

## What happens if a page or listing is unavailable?

Some content may be removed, unpublished, sold out, or temporarily unavailable. If a public page or listing is not currently published, it may return a not found response.

## Where can I read the official rules and policies?

You can review the following pages for full details:
- Terms & Conditions
- Privacy Policy
- Auction Rules
- Buying Rules

## How can I contact Mineral Kingdom?

For policy or privacy questions, the current contact listed in the CMS policy content is:

**Brad Holland**  
**admin@mineral-kingdom.com**
""";

        private static string PrivacyMarkdown() => """
# Mineral Kingdom Privacy Policy

At mineral-kingdom.com (Mineral Kingdom), our commitment is to safeguard the privacy and security of our customers and site visitors. We recognize and respect the significance of data privacy and security on the Internet.

This Privacy Policy describes our policies and procedures on the collection, use and disclosure of your information when you use the service and tells you about your privacy rights and how the law protects you.

We use your personal data to provide and improve the service. By using the service, you agree to the collection and use of information in accordance with this Privacy Policy.

## Collection & Use of Personal Data

During each site visit, our servers may temporarily store access data in a protocol file. This data may include the originating IP address, date and time of arrival and departure, entry URL, referring website or search engine, pages visited, device operating system and browser used, country, and Internet Service Provider.

Upon registration, a customer account requires information such as email address, username, password, and address. Additional data may be collected on a voluntary basis. This data is necessary for user verification, account administration, contractual performance, and communication with Mineral Kingdom.

## Passing Data to Third Parties

We will not pass on your data unless we have received your agreement, are limited by legal requirements, or disclosure is necessary for the assertion of our rights or contractual obligations.

## Right to Access, Correct & Delete Personal Information

Personal data may be accessible through user login or in writing with acceptable authentication. Modification or deletion of data may be managed through user login or by applying in writing. Certain purchase-related data may be retained where required by contract or law.

## Data Security

Mineral Kingdom protects user and other data through technical and operational safeguards that are continuously monitored and updated. User accounts are accessible only through a personal password, and it is the user's responsibility to keep passwords secure. Mineral Kingdom does not store debit card, credit card, or bank payment information directly.

## Cookies

Cookies may be used on Mineral Kingdom to enhance user experience and remember site preferences. Users may be presented with cookie consent options before non-essential cookies are placed.

## Keeping Your Information

We retain your information as long as you have an account or as needed to provide Mineral Kingdom services. If required by law, for lawful requirements, dispute resolution, fraud prevention, or enforcing terms and conditions, we may keep some information even after you close your account.

## Contact

For questions about data privacy on Mineral Kingdom or our services, or information and deletion requests, contact Brad Holland at admin@mineral-kingdom.com.

## Validity & Changes to our Privacy Policy

Our data privacy guidelines are currently valid from February 2024. We may update and notify users of material changes as Mineral Kingdom services evolve or the law changes.
""";

        private static string TermsMarkdown() => """
# Terms & Conditions

This document outlines your rights and obligations, as well as those of mineral-kingdom.com ("Mineral Kingdom," "we," or "us"), regarding the services provided through the mineral-kingdom.com site.

Registration for these services may be free, but becoming a member and using Mineral Kingdom services means agreeing to these terms, together with the Auction Rules, Buying Rules, and Privacy Policy.

## Registration

You agree to register for Mineral Kingdom services using your correct name, address, and other requested details. If any information becomes incorrect, you must update your Mineral Kingdom account before continuing to bid or purchase.

To enter into any transaction, you must be legally able to enter into contracts for the sale or purchase of the items in question.

## Use of Personal Data

We gather, use, and disclose personal, contact, and payment-related data in accordance with our Privacy Policy. Mineral Kingdom may use cookies or similar technical means to personalize your experience.

## Transactions

Mineral Kingdom may offer auction transactions and fixed-price buying transactions. You must read and understand the relevant transaction rules before participating.

Mineral Kingdom must accurately describe each item in a listing and is responsible for material errors or omissions in those descriptions.

## Intellectual Property

All copyright and other intellectual property rights relating to Mineral Kingdom services are owned by Mineral Kingdom. You must not use those rights except as necessary to engage in authorized activity on the service.

## Management

Mineral Kingdom reserves the right to cancel or withdraw listings, to close or extend transactions, or to suspend or terminate services where there are compelling lawful or technical reasons to do so.

Mineral Kingdom may suspend or terminate the account of any member found to be continuously or materially failing to follow this agreement.

## Accountability

To the degree permissible by law, Mineral Kingdom disclaims liability arising from listings or transactions except where liability cannot be excluded by law.

Mineral Kingdom's liability under this agreement will not exceed the greater of $100 or the sums paid or payable in the relevant transaction or transactions.

You agree to indemnify Mineral Kingdom against liabilities, claims, and expenses arising from any breach of this agreement by you or through your account.

## Disclaimer

The contents of this website are not to be considered any express or implied warranty or representation of any kind. All minerals of Mineral Kingdom, including those on this website, are sold "As Is". Price and availability are subject to change at any time.

## Delegation

Mineral Kingdom may assign or subcontract any or all of its rights and obligations under this agreement. If any term is deemed invalid or unenforceable, the remainder of the agreement will remain effective.
""";

        private static string AuctionRulesMarkdown() => """
# Auction Rules

## Introduction

These Auction Rules outline the regulations governing auction transactions accessible through Mineral Kingdom services, the specific types of auctions available, and particular rules associated with them.

These Auction Rules are legally binding and form part of your agreement with Mineral Kingdom. By participating in an auction, you acknowledge that you have read, understood, and accepted these rules.

## Bids

Bids placed in auctions on Mineral Kingdom are binding. Once a bid is placed through your account, it may not normally be withdrawn and may become legally binding if successful.

Due to potential internet connectivity issues, receipt of the bid by the Mineral Kingdom system constitutes the bid, not dispatch from the bidder's device.

## Currency

The base currency for auctions is United States Dollars (USD), and amounts due are payable in that currency.

## Types of Bids

Mineral Kingdom may support:
- standard auction bidding
- proxy bidding
- delayed bidding

Proxy bidding allows a member to enter a maximum bid and have Mineral Kingdom bid on their behalf as needed, subject to reserve and competing bids.

## Bid Increments

Bids are placed in whole dollar increments. Increment levels vary by current auction bid level.

## Auction Procedures

Auctions run until their specified close time and may enter an extended closing sequence if additional bids are received near the end. Reserve-price and timing rules apply according to the listing and platform rules.

If the reserve price is not met once the auction ends, the item may be relisted.

## Reserve Price

Any item may be set with a hidden reserve price. If the reserve is not met, the item is not sold.

## Buyer Emails

Mineral Kingdom may provide mandatory and optional auction-related emails, including bid acceptance, winning bid, outbid notifications, and auction clock reminders.

## Auction Clock

Auctions start, continue, and finish according to the time controlled by the Mineral Kingdom system clock. Browser clocks may differ, and Mineral Kingdom is not responsible for device-side timing variance.

## Bid Retraction

All bids must be considered carefully, as bids are generally binding and retractions are rarely allowed.

Exceptional circumstances may include typographical errors, significant listing changes, or illicit account use. Bids may not be retracted within the last hour of an auction.

## Accountability

Mineral Kingdom may monitor bid retractions, and abuse may result in account suspension.

To request bid retraction, use the Contact Administrator form on the item auction page.
""";

        private static string BuyingRulesMarkdown() => """
# Buying Rules

## Introduction

These Buying Rules govern fixed-price, non-auction transactions conducted through Mineral Kingdom.

These rules are legally binding and form part of your agreement with Mineral Kingdom. By making a purchase, you acknowledge that you have read, understood, and accepted these rules.

## Purchases

Items selected for purchase may first be added to a cart system within your account when you click BUY NOW.

Adding items to the cart does not itself constitute an agreement to purchase, nor does it guarantee the item will be sold to you.

Clicking BUY NOW reserves an item for a limited time. If checkout is not completed during that period, the item may become available again.

A binding agreement to purchase is formed when you complete checkout and payment, or otherwise confirm the order for invoicing as allowed by Mineral Kingdom.

Confirmed purchases are binding.

## Currency

Listings are generally priced in US dollars, and amounts due are payable in that currency unless otherwise agreed.

Currency conversions shown for convenience may vary from the actual exchange rate applied by the buyer's bank or payment provider.

## Purchase Retraction

All purchases must be considered carefully, as confirmed orders are binding.

Exceptional circumstances may include a significant change to the description of an item or illicit use of your user ID and password.

## Accountability

Mineral Kingdom may monitor order retractions, and abuse may result in account suspension.

To retract an order, use the Contact Administrator form on the item page.
""";
    }
}