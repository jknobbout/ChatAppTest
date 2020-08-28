using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Text;
using System.Threading;

internal class Chat
{
    // Set exchange name for chat channels to connect to
    private static readonly string exchangeName = "chatTestExchange";

    // Set the name of the queue that stores all sent messages
    private static readonly string storageQueueName = "storedMessagesQueue";

    // Declare these variables outside the Main method so other methods can use them too
    private static string chatName;
    private static IConnection connection;
    private static string randCorrelationId;

    public static void Main()
    {
        ConnectToServer();

        Console.Clear();
        Console.Title = "Chat App Test";

        // Get chat name from user
        chatName = GetUserInput("Enter a name to use while chatting: ");

        // Create persistent queue to which all messages are sent (won't be overwritten if one already exists)
        StoreFutureMessages();

        // Check if user wants to see previous messages
        AskForPreviousMessages(false);
        
        // Create two separate threads for sending/receiving live messages
        new Thread(new ThreadStart(StartSending)).Start();
        new Thread(new ThreadStart(StartReceiving)).Start();
    }

    public static void ConnectToServer()
    {
        // Set server settings here, comment out settings that aren't needed
        ConnectionFactory factory = new ConnectionFactory()
        {
            HostName = "localhost"
            //VirtualHost = virtualHost,
            //UserName = userName,
            //Password = password
        };

        connection = factory.CreateConnection();
    }

    public static void StoreFutureMessages()
    {
        // Create new channel, declare it to the fanout exchange, declare new queue and bind it to the exchange.
        // Any messages sent by users will therefore be sent to this persistent queue as well.
        IModel channelSaveStoredMsgs = CreateChannel(connection, exchangeName);

        channelSaveStoredMsgs.QueueDeclare(queue: storageQueueName,
                                 durable: true,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);

        channelSaveStoredMsgs.QueueBind(queue: storageQueueName,
                                 exchange: exchangeName,
                                 routingKey: "");
    }

    public static void AskForPreviousMessages(bool retry)
    {
        string getMsgs;
        if (!retry)
        {
            getMsgs = GetUserInput("Do you want to retrieve previous conversations? (y/n): ");
        }
        else 
        {
            getMsgs = GetUserInput("Please enter either 'y' or 'n': ");
        }

        if (getMsgs == "y")
        {
            // Use main thread to retrieve previously stored messages
            GetStoredMessages();
        }
        else if (getMsgs != "n")
        {
            AskForPreviousMessages(true);
        }
    }

    public static void GetStoredMessages()
    {
        IModel channelGetStoredMsgs = CreateChannel(connection, exchangeName);
        // Create new consumer that will retrieve the messages from the storage queue without acknowledging them,
        // so they can be retrieved by other chatters as well.
        EventingBasicConsumer consumerStored = new EventingBasicConsumer(channelGetStoredMsgs);

        channelGetStoredMsgs.BasicConsume(queue: storageQueueName,
                             autoAck: false,
                             consumer: consumerStored);

        Console.WriteLine("\nHi {0}, previous messages are being retrieved... \n", chatName);

        // Keep count of number of messages that are retrieved
        int nMessages = 0;
        consumerStored.Received += (model, ea) =>
        {
            byte[] body = ea.Body.ToArray();
            string message = Encoding.UTF8.GetString(body);
            Console.WriteLine(message);
            nMessages++;
        };

        // Wait for all messages to load with a little shortcut
        Thread.Sleep(1000);

        // Close the channel when the messages have been retrieved (or if no messages were found)
        channelGetStoredMsgs.Close();

        if (nMessages > 0)
        {
            Console.WriteLine("\nPrevious messages retrieved!\n");
        }
        else
        {
            Console.WriteLine("No previous messages were found.\n");
        }
    }

    public static void StartSending()
    {
        // Create new channel for sending messages and bind it to the fanout exchange
        IModel channelSend = CreateChannel(connection, exchangeName);

        Console.WriteLine("You can start chatting! Type 'exit' to stop chatting.\n");

        // Add a correlationID to sent messages. This can be used to filter incoming messages from the user who sent them.
        randCorrelationId = new Random().Next().ToString();
        var properties = channelSend.CreateBasicProperties();
        properties.CorrelationId = randCorrelationId;

        // Start a loop to keep listening for user input
        while (true)
        {
            Console.Write(chatName + ": ");

            // Listen for new messages from user input
            string message = Console.ReadLine();

            // Listen for 'exit' to stop chatting
            if (message == "exit")
            {
                break;
            }

            // Add timestamp to the message
            string formattedMsg = FormatMessage(message);

            // Send message to the exchange without specifying a queue (fanout exchange sends it to all connected
            // queues.
            byte[] body = Encoding.UTF8.GetBytes(formattedMsg);
            channelSend.BasicPublish(exchange: exchangeName,
                                    routingKey: "",
                                    basicProperties: properties,
                                    body: body);

            // Write the sent message over the console input to prevent double messages from appearing
            Console.SetCursorPosition(0, Console.CursorTop - 1);
            Console.WriteLine(formattedMsg);
        }
    }

    public static void StartReceiving()
    {
        // Create new channel for receiving live messages and bind it to the exchange
        IModel channelReceive = CreateChannel(connection, exchangeName);

        // Create a temporary queue to receive live messages and bind it to the exchange
        string tempQueueName = channelReceive.QueueDeclare().QueueName;

        channelReceive.QueueBind(queue: tempQueueName,
                                 exchange: exchangeName,
                                 routingKey: "");

        // Start receiving live messages
        EventingBasicConsumer consumer = new EventingBasicConsumer(channelReceive);

        channelReceive.BasicConsume(queue: tempQueueName,
                             autoAck: true,
                             consumer: consumer);

        consumer.Received += (model, ea) =>
        {
            byte[] body = ea.Body.ToArray();

            string message = Encoding.UTF8.GetString(body);

            // Messages sent by the user are received by the user too, so filter out those 
            // messages before writing them to the console to prevent double messages from appearing
            if (ea.BasicProperties.CorrelationId != randCorrelationId)
            {
                Console.WriteLine("\r" + message);
                Console.Write(chatName + ": ");
            }
        };
    }

    public static string FormatMessage(string message)
    {
        string currentTime = DateTime.Now.ToString().Split(" ")[1];
        return currentTime + " " + chatName + ": " + message;
    }

    public static string GetUserInput(string question)
    {
        Console.Write(question);
        return Console.ReadLine();
    }

    public static IModel CreateChannel(IConnection c, string exchange)
    {
        IModel channel = c.CreateModel();
        channel.ExchangeDeclare(exchange, type: ExchangeType.Fanout);
        return channel;
    }

    
}