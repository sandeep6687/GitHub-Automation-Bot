using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GitHubBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "connected_repositories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    github_repository_id = table.Column<long>(type: "bigint", nullable: false),
                    full_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    owner = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    default_branch = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "main"),
                    webhook_id = table.Column<long>(type: "bigint", nullable: true),
                    encrypted_webhook_secret = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connected_repositories", x => x.id);
                    table.ForeignKey(
                        name: "FK_connected_repositories_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "github_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    github_user_id = table.Column<long>(type: "bigint", nullable: false),
                    login = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    avatar_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    encrypted_access_token = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_github_accounts", x => x.id);
                    table.ForeignKey(
                        name: "FK_github_accounts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_rules_connected_repositories_repository_id",
                        column: x => x.repository_id,
                        principalTable: "connected_repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "webhook_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    raw_payload = table.Column<string>(type: "jsonb", nullable: false),
                    parsed_data = table.Column<string>(type: "jsonb", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 6),
                    next_retry_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    claimed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    processed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_webhook_events_connected_repositories_repository_id",
                        column: x => x.repository_id,
                        principalTable: "connected_repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rule_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    configuration = table.Column<string>(type: "jsonb", nullable: false),
                    execution_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rule_actions", x => x.id);
                    table.ForeignKey(
                        name: "FK_rule_actions_rules_rule_id",
                        column: x => x.rule_id,
                        principalTable: "rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rule_conditions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    condition_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    case_sensitive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rule_conditions", x => x.id);
                    table.ForeignKey(
                        name: "FK_rule_conditions_rules_rule_id",
                        column: x => x.rule_id,
                        principalTable: "rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "action_executions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    webhook_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_action_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    request_payload = table.Column<string>(type: "jsonb", nullable: true),
                    response_payload = table.Column<string>(type: "jsonb", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    attempt_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    executed_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_executions", x => x.id);
                    table.ForeignKey(
                        name: "FK_action_executions_rule_actions_rule_action_id",
                        column: x => x.rule_action_id,
                        principalTable: "rule_actions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_action_executions_webhook_events_webhook_event_id",
                        column: x => x.webhook_event_id,
                        principalTable: "webhook_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_action_executions_lookup",
                table: "action_executions",
                columns: new[] { "webhook_event_id", "rule_action_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_action_executions_rule_action_id",
                table: "action_executions",
                column: "rule_action_id");

            migrationBuilder.CreateIndex(
                name: "uq_action_executions_event_rule_action",
                table: "action_executions",
                columns: new[] { "webhook_event_id", "rule_action_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_connected_repositories_full_name",
                table: "connected_repositories",
                column: "full_name");

            migrationBuilder.CreateIndex(
                name: "IX_connected_repositories_user_id",
                table: "connected_repositories",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_connected_repositories_github_repo_id",
                table: "connected_repositories",
                column: "github_repository_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_github_accounts_github_user_id",
                table: "github_accounts",
                column: "github_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_github_accounts_user_id",
                table: "github_accounts",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rule_actions_rule_id",
                table: "rule_actions",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "IX_rule_conditions_rule_id",
                table: "rule_conditions",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "idx_rules_repo_event",
                table: "rules",
                columns: new[] { "repository_id", "event_type" },
                filter: "is_enabled = TRUE");

            migrationBuilder.CreateIndex(
                name: "idx_webhook_events_claimable",
                table: "webhook_events",
                column: "created_at",
                filter: "status IN ('Pending', 'Retrying')");

            migrationBuilder.CreateIndex(
                name: "idx_webhook_events_stale_claims",
                table: "webhook_events",
                column: "claimed_at",
                filter: "status = 'Processing'");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_events_repository_id",
                table: "webhook_events",
                column: "repository_id");

            migrationBuilder.CreateIndex(
                name: "uq_webhook_events_delivery_id",
                table: "webhook_events",
                column: "delivery_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "action_executions");

            migrationBuilder.DropTable(
                name: "github_accounts");

            migrationBuilder.DropTable(
                name: "rule_conditions");

            migrationBuilder.DropTable(
                name: "rule_actions");

            migrationBuilder.DropTable(
                name: "webhook_events");

            migrationBuilder.DropTable(
                name: "rules");

            migrationBuilder.DropTable(
                name: "connected_repositories");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
