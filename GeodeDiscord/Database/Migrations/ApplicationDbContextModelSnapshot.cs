﻿// <auto-generated />
using System;
using GeodeDiscord.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace GeodeDiscord.Database.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    partial class ApplicationDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "9.0.7");

            modelBuilder.Entity("GeodeDiscord.Database.Entities.Guess", b =>
                {
                    b.Property<ulong>("messageId")
                        .HasColumnType("INTEGER");

                    b.Property<ulong>("guessId")
                        .HasColumnType("INTEGER");

                    b.Property<DateTimeOffset>("guessedAt")
                        .HasColumnType("TEXT");

                    b.Property<ulong>("quoteMessageId")
                        .HasColumnType("INTEGER");

                    b.Property<ulong>("userId")
                        .HasColumnType("INTEGER");

                    b.HasKey("messageId");

                    b.HasIndex("guessedAt");

                    b.HasIndex("quoteMessageId");

                    b.HasIndex("userId");

                    b.ToTable("guesses", (string)null);
                });

            modelBuilder.Entity("GeodeDiscord.Database.Entities.Quote", b =>
                {
                    b.Property<ulong>("messageId")
                        .HasColumnType("INTEGER");

                    b.Property<ulong>("authorId")
                        .HasColumnType("INTEGER");

                    b.Property<ulong>("channelId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("content")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<DateTimeOffset>("createdAt")
                        .HasColumnType("TEXT");

                    b.Property<int>("extraAttachments")
                        .HasColumnType("INTEGER");

                    b.Property<int>("id")
                        .HasColumnType("INTEGER");

                    b.Property<string>("images")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("jumpUrl")
                        .HasColumnType("TEXT");

                    b.Property<DateTimeOffset>("lastEditedAt")
                        .HasColumnType("TEXT");

                    b.Property<string>("name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<ulong>("quoterId")
                        .HasColumnType("INTEGER");

                    b.Property<ulong>("replyAuthorId")
                        .HasColumnType("INTEGER");

                    b.HasKey("messageId");

                    b.HasIndex("authorId");

                    b.HasIndex("id")
                        .IsUnique();

                    b.HasIndex("name");

                    b.ToTable("quotes", (string)null);
                });

            modelBuilder.Entity("GeodeDiscord.Database.Entities.StickyRole", b =>
                {
                    b.Property<ulong>("userId")
                        .HasColumnType("INTEGER");

                    b.Property<ulong>("roleId")
                        .HasColumnType("INTEGER");

                    b.HasKey("userId", "roleId");

                    b.HasIndex("roleId");

                    b.HasIndex("userId");

                    b.ToTable("stickyRoles", (string)null);
                });

            modelBuilder.Entity("GeodeDiscord.Database.Entities.Guess", b =>
                {
                    b.HasOne("GeodeDiscord.Database.Entities.Quote", "quote")
                        .WithMany()
                        .HasForeignKey("quoteMessageId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("quote");
                });
#pragma warning restore 612, 618
        }
    }
}
