﻿// ==========================================================================
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex UG (haftungsbeschränkt)
//  All rights reserved. Licensed under the MIT license.
// ==========================================================================

using Squidex.Infrastructure;

namespace Squidex.Domain.Apps.Entities.Contents.Commands
{
    public sealed class CreateContent : ContentDataCommand, ISchemaCommand, IAppCommand
    {
        public NamedId<DomainId> AppId { get; set; }

        public NamedId<DomainId> SchemaId { get; set; }

        public bool Publish { get; set; }

        public CreateContent()
        {
            ContentId = DomainId.NewGuid();
        }
    }
}
