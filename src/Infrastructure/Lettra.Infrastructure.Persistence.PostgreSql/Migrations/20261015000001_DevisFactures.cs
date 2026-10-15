using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class DevisFactures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LegalForm = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ShareCapital = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Siren = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    Siret = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    Rcs = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VatNumber = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    VatFranchise = table.Column<bool>(type: "boolean", nullable: false),
                    VatOnDebits = table.Column<bool>(type: "boolean", nullable: false),
                    PaymentDays = table.Column<int>(type: "integer", nullable: false),
                    PaymentTerms = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LatePenaltyRate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    EarlyPaymentDiscount = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    Bic = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    City = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    AddressLine1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AddressLine2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyProfile", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SellerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SellerAddressLine1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SellerAddressLine2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SellerPostalCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    SellerCity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SellerCountry = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    SellerSiren = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    SellerSiret = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    SellerVatNumber = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    SellerLegalForm = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SellerShareCapital = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SellerRcs = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SellerIsBusiness = table.Column<bool>(type: "boolean", nullable: true),
                    DeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PaymentTerms = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DeliveryAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PurchaseOrderReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LatePenaltyRate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    EarlyPaymentDiscount = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuoteNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreditedInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreditedInvoiceNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreditReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreditedInclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IssuedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SeriesCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustomerAddressLine1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustomerAddressLine2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomerPostalCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CustomerCity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustomerCountry = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    CustomerSiren = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    CustomerSiret = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    CustomerVatNumber = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    CustomerLegalForm = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CustomerShareCapital = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    CustomerRcs = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CustomerIsBusiness = table.Column<bool>(type: "boolean", nullable: false),
                    VatRegime = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TotalExclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalVat = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalInclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LegalMentions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PriceLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Quotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    DownPaymentsInclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    FinalInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SeriesCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustomerAddressLine1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustomerAddressLine2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomerPostalCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CustomerCity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustomerCountry = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    CustomerSiren = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    CustomerSiret = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    CustomerVatNumber = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    CustomerLegalForm = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CustomerShareCapital = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    CustomerRcs = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CustomerIsBusiness = table.Column<bool>(type: "boolean", nullable: false),
                    VatRegime = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TotalExclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalVat = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalInclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LegalMentions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalesJournalEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PieceNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesJournalEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                columns: table => new
                {
                    Position = table.Column<int>(type: "integer", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Designation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Nature = table.Column<int>(type: "integer", nullable: false),
                    UnitCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPriceExclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRateCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    VatRatePercent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    VatCategory = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    VatExemptionCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    TotalExclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => new { x.DocumentId, x.Position });
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceVatSummaries",
                columns: table => new
                {
                    Category = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    RatePercent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseExclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ExemptionCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceVatSummaries", x => new { x.DocumentId, x.Category, x.RatePercent });
                    table.ForeignKey(
                        name: "FK_InvoiceVatSummaries_Invoices_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PriceListCustomers",
                columns: table => new
                {
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceListId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceListCustomers", x => new { x.PriceListId, x.CustomerId });
                    table.ForeignKey(
                        name: "FK_PriceListCustomers_PriceLists_PriceListId",
                        column: x => x.PriceListId,
                        principalTable: "PriceLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PriceListEntries",
                columns: table => new
                {
                    CatalogItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceListId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitPriceExclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    DiscountPercent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceListEntries", x => new { x.PriceListId, x.CatalogItemId });
                    table.ForeignKey(
                        name: "FK_PriceListEntries_PriceLists_PriceListId",
                        column: x => x.PriceListId,
                        principalTable: "PriceLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuoteLines",
                columns: table => new
                {
                    Position = table.Column<int>(type: "integer", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Designation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Nature = table.Column<int>(type: "integer", nullable: false),
                    UnitCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPriceExclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRateCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    VatRatePercent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    VatCategory = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    VatExemptionCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    TotalExclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteLines", x => new { x.DocumentId, x.Position });
                    table.ForeignKey(
                        name: "FK_QuoteLines_Quotes_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuoteVatSummaries",
                columns: table => new
                {
                    Category = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    RatePercent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseExclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ExemptionCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteVatSummaries", x => new { x.DocumentId, x.Category, x.RatePercent });
                    table.ForeignKey(
                        name: "FK_QuoteVatSummaries_Quotes_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesJournalLines",
                columns: table => new
                {
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    Account = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Debit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRatePercent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesJournalLines", x => new { x.EntryId, x.LineNumber });
                    table.ForeignKey(
                        name: "FK_SalesJournalLines_SalesJournalEntries_EntryId",
                        column: x => x.EntryId,
                        principalTable: "SalesJournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CreditedInvoiceId",
                table: "Invoices",
                column: "CreditedInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CustomerId",
                table: "Invoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_IssueDate",
                table: "Invoices",
                column: "IssueDate");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Kind_Status",
                table: "Invoices",
                columns: new[] { "Kind", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_QuoteId",
                table: "Invoices",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SeriesCode_Number",
                table: "Invoices",
                columns: new[] { "SeriesCode", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceListCustomers_CustomerId",
                table: "PriceListCustomers",
                column: "CustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_CustomerId",
                table: "Quotes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_IssueDate",
                table: "Quotes",
                column: "IssueDate");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_SeriesCode_Number",
                table: "Quotes",
                columns: new[] { "SeriesCode", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_Status",
                table: "Quotes",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SalesJournalEntries_Date",
                table: "SalesJournalEntries",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_SalesJournalEntries_InvoiceId",
                table: "SalesJournalEntries",
                column: "InvoiceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyProfile");

            migrationBuilder.DropTable(
                name: "InvoiceLines");

            migrationBuilder.DropTable(
                name: "InvoiceVatSummaries");

            migrationBuilder.DropTable(
                name: "PriceListCustomers");

            migrationBuilder.DropTable(
                name: "PriceListEntries");

            migrationBuilder.DropTable(
                name: "QuoteLines");

            migrationBuilder.DropTable(
                name: "QuoteVatSummaries");

            migrationBuilder.DropTable(
                name: "SalesJournalLines");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "PriceLists");

            migrationBuilder.DropTable(
                name: "Quotes");

            migrationBuilder.DropTable(
                name: "SalesJournalEntries");
        }
    }
}
