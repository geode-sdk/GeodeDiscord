﻿// <auto-generated />
using System;
using GeodeDiscord.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace GeodeDiscord.Database.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20250717161609_SeparateQuoteIds")]
    partial class SeparateQuoteIds
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "9.0.7");

            modelBuilder.Entity("GeodeDiscord.Database.Entities.GuessStats", b =>
                {
                    b.Property<ulong>("userId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<ulong>("correct")
                        .HasColumnType("INTEGER");

                    b.Property<ulong>("maxStreak")
                        .HasColumnType("INTEGER");

                    b.Property<ulong>("streak")
                        .HasColumnType("INTEGER");

                    b.Property<ulong>("total")
                        .HasColumnType("INTEGER");

                    b.HasKey("userId");

                    b.ToTable("guessStats");
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

                    b.ToTable("quotes");
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

                    b.ToTable("stickyRoles");
                });
#pragma warning restore 612, 618
        }
    }
}
